/*
 * Renders a Smithy list/set as a C# wrapper record over IReadOnlyList<T>.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.SymbolProperties;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ListGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ListShape shape;

  public ListGenerator(GenerationContext c, CSharpWriter w, ListShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    Symbol member = sp.toSymbol(context.model().expectShape(shape.getMember().getTarget()));
    String memberType =
        CSharpSymbolProvider.qualified(member) + (ShapeSupport.isSparse(shape) ? "?" : "");

    writer.write(
        "public sealed partial record class $L : ISerializableShape, IDeserializableShape<$L>",
        typeName,
        typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("");
          SchemaGenerator.writeListSchema(writer, context, shape);
          writer.write("Schema ISerializableShape.Schema => Schema;");
          writer.write("");
          writer.write(
              "public $L(System.Collections.Generic.IEnumerable<$L> values)", typeName, memberType);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(values);");
                writer.write(
                    "Values = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(values));");
              });
          writer.write("");
          writer.write(
              "public System.Collections.Generic.IReadOnlyList<$L> Values { get; }", memberType);
          writer.write("");
          writeSerialize();
          writer.write("");
          writeDeserialize(typeName, memberType);
        });
  }

  private void writeSerialize() {
    Shape target = context.model().expectShape(shape.getMember().getTarget());
    Symbol memberSym = context.symbolProvider().toSymbol(target);
    boolean memberIsValueType =
        memberSym.getProperty(SymbolProperties.IS_VALUE_TYPE, Boolean.class).orElse(false);
    writer.write("public void Serialize(IShapeSerializer serializer)");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
          writer.write("serializer.WriteList(Schema, Values, Values.Count, static (values, w) =>");
          writer.openBlock(
              "{",
              "});",
              () -> {
                writer.write("foreach (var item in values)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      if (ShapeSupport.isSparse(shape)) {
                        writer.write("if (item is null)");
                        writer.openBlock(
                            "{", "}", () -> writer.write("w.WriteNull(MemberSchema);"));
                        writer.write("else");
                        writer.openBlock(
                            "{",
                            "}",
                            () ->
                                writer.write(
                                    writeValueStatement(
                                        target,
                                        "w",
                                        "MemberSchema",
                                        memberIsValueType ? "item.Value" : "item")));
                      } else {
                        writer.write(writeValueStatement(target, "w", "MemberSchema", "item"));
                      }
                    });
              });
        });
  }

  private void writeDeserialize(String typeName, String memberType) {
    Shape target = context.model().expectShape(shape.getMember().getTarget());
    writer.write("public static $L Deserialize(IShapeDeserializer deserializer)", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(deserializer);");
          writer.write("var values = new System.Collections.Generic.List<$L>();", memberType);
          writer.write("deserializer.ReadList(Schema, values, static (list, r) =>");
          writer.openBlock(
              "{",
              "});",
              () -> {
                if (ShapeSupport.isSparse(shape)) {
                  writer.write("if (r.IsNull())");
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        writer.write("r.ReadNull();");
                        writer.write("list.Add(null);");
                      });
                  writer.write("else");
                  writer.openBlock(
                      "{",
                      "}",
                      () ->
                          writer.write(
                              "list.Add("
                                  + readValueExpression(target, "r", "MemberSchema")
                                  + ");"));
                } else {
                  writer.write(
                      "list.Add(" + readValueExpression(target, "r", "MemberSchema") + ");");
                }
              });
          writer.write("return new $L(values);", typeName);
        });
  }

  private String writeValueStatement(
      Shape target, String serializerVar, String schemaVar, String valueExpr) {
    return switch (target.getType()) {
      case BOOLEAN -> serializerVar + ".WriteBoolean(" + schemaVar + ", " + valueExpr + ");";
      case BYTE -> serializerVar + ".WriteByte(" + schemaVar + ", " + valueExpr + ");";
      case SHORT -> serializerVar + ".WriteShort(" + schemaVar + ", " + valueExpr + ");";
      case INTEGER -> serializerVar + ".WriteInteger(" + schemaVar + ", " + valueExpr + ");";
      case LONG -> serializerVar + ".WriteLong(" + schemaVar + ", " + valueExpr + ");";
      case FLOAT -> serializerVar + ".WriteFloat(" + schemaVar + ", " + valueExpr + ");";
      case DOUBLE -> serializerVar + ".WriteDouble(" + schemaVar + ", " + valueExpr + ");";
      case BIG_INTEGER -> serializerVar + ".WriteBigInteger(" + schemaVar + ", " + valueExpr + ");";
      case BIG_DECIMAL -> serializerVar + ".WriteBigDecimal(" + schemaVar + ", " + valueExpr + ");";
      case TIMESTAMP -> serializerVar + ".WriteTimestamp(" + schemaVar + ", " + valueExpr + ");";
      case STRING -> serializerVar + ".WriteString(" + schemaVar + ", " + valueExpr + ");";
      case ENUM -> serializerVar + ".WriteString(" + schemaVar + ", " + valueExpr + ".Value);";
      case BLOB -> serializerVar + ".WriteBlob(" + schemaVar + ", " + valueExpr + ");";
      case DOCUMENT -> serializerVar + ".WriteDocument(" + schemaVar + ", " + valueExpr + ");";
      case INT_ENUM -> serializerVar + ".WriteInteger(" + schemaVar + ", (int)" + valueExpr + ");";
      case STRUCTURE, UNION, LIST, SET, MAP -> valueExpr + ".Serialize(" + serializerVar + ");";
      default ->
          throw new IllegalArgumentException("Unsupported list member shape: " + target.getId());
    };
  }

  private String readValueExpression(Shape target, String deserializerVar, String schemaVar) {
    return switch (target.getType()) {
      case BOOLEAN -> deserializerVar + ".ReadBoolean(" + schemaVar + ")";
      case BYTE -> deserializerVar + ".ReadByte(" + schemaVar + ")";
      case SHORT -> deserializerVar + ".ReadShort(" + schemaVar + ")";
      case INTEGER -> deserializerVar + ".ReadInteger(" + schemaVar + ")";
      case LONG -> deserializerVar + ".ReadLong(" + schemaVar + ")";
      case FLOAT -> deserializerVar + ".ReadFloat(" + schemaVar + ")";
      case DOUBLE -> deserializerVar + ".ReadDouble(" + schemaVar + ")";
      case BIG_INTEGER -> deserializerVar + ".ReadBigInteger(" + schemaVar + ")";
      case BIG_DECIMAL -> deserializerVar + ".ReadBigDecimal(" + schemaVar + ")";
      case TIMESTAMP -> deserializerVar + ".ReadTimestamp(" + schemaVar + ")";
      case STRING -> deserializerVar + ".ReadString(" + schemaVar + ")";
      case BLOB -> deserializerVar + ".ReadBlob(" + schemaVar + ")";
      case DOCUMENT -> deserializerVar + ".ReadDocument(" + schemaVar + ")";
      case ENUM, STRUCTURE, UNION, LIST, SET, MAP ->
          CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target))
              + ".Deserialize("
              + deserializerVar
              + ")";
      case INT_ENUM ->
          "("
              + CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target))
              + ")"
              + deserializerVar
              + ".ReadInteger("
              + schemaVar
              + ")";
      default ->
          throw new IllegalArgumentException("Unsupported list member shape: " + target.getId());
    };
  }
}
