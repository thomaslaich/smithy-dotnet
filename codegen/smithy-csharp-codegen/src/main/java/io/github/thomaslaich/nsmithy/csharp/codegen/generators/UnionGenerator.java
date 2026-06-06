/*
 * Renders a Smithy union as a C# abstract record class with a sealed nested
 * record per variant plus a `Match` method for pattern-matching consumers.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class UnionGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final UnionShape shape;

  public UnionGenerator(GenerationContext c, CSharpWriter w, UnionShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    Model model = context.model();
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    List<MemberShape> members = ShapeSupport.sortedMembers(shape);

    writer.write(
        "public abstract partial record class $L : ISerializableShape, IDeserializableShape<$L>",
        typeName,
        typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          SchemaGenerator.writeStructureSchema(writer, context, shape, members);
          writer.write("Schema ISerializableShape.Schema => Schema;");
          writer.write("");
          writer.write("private protected $L() { }", typeName);
          writer.write("");
          writer.write("public abstract void Serialize(IShapeSerializer serializer);");
          writer.write(
              "public abstract void Serialize(IShapeSerializer serializer, Schema schema);");
          writer.write("");
          for (MemberShape m : members) {
            String variantName = CSharpNaming.typeName(m.getMemberName());
            String valueType = ShapeSupport.memberTypeExpr(sp, m, false);
            Shape target = model.expectShape(m.getTarget());
            writer.write("public sealed partial record class $L : $L", variantName, typeName);
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writer.write("public $L($L value)", variantName, valueType);
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        if (ShapeSupport.isReferenceType(model, m)) {
                          writer.write(
                              "Value = value ?? throw new"
                                  + " System.ArgumentNullException(nameof(value));");
                        } else {
                          writer.write("Value = value;");
                        }
                      });
                  writer.write("");
                  writer.write("public $L Value { get; }", valueType);
                  writer.write("");
                  writer.write("public override void Serialize(IShapeSerializer serializer)");
                  writer.openBlock("{", "}", () -> writer.write("Serialize(serializer, Schema);"));
                  writer.write("");
                  writer.write(
                      "public override void Serialize(IShapeSerializer serializer, Schema schema)");
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
                        writer.write("serializer.WriteStruct(schema, new UnionMembers(this));");
                      });
                  writer.write("");
                  writer.write(
                      "private sealed class UnionMembers($L owner) : ISerializableStruct",
                      variantName);
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        writer.write("public Schema Schema => $L.Schema;", typeName);
                        writer.write("");
                        writer.write("public void Serialize(IShapeSerializer serializer)");
                        writer.openBlock(
                            "{", "}", () -> writer.write("serializer.WriteStruct(Schema, this);"));
                        writer.write("");
                        writer.write("public void SerializeMembers(IShapeSerializer serializer)");
                        writer.openBlock(
                            "{",
                            "}",
                            () ->
                                writer.write(
                                    writeValueStatement(
                                        target,
                                        "serializer",
                                        variantName + "Schema",
                                        "owner.Value")));
                      });
                });
            writer.write("");
            writer.write("public static $L From$L($L value)", typeName, variantName, valueType);
            writer.openBlock("{", "}", () -> writer.write("return new $L(value);", variantName));
            writer.write("");
          }

          writer.addImport(RuntimeTypes.NSMITHY_CORE);
          writer.write("public sealed partial record class Unknown : $L", typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("public Unknown(string tag, Document value)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write(
                          "Tag = tag ?? throw new System.ArgumentNullException(nameof(tag));");
                      writer.write("Value = value;");
                    });
                writer.write("");
                writer.write("public string Tag { get; }");
                writer.write("public Document Value { get; }");
                writer.write("");
                writer.write("public override void Serialize(IShapeSerializer serializer)");
                writer.openBlock("{", "}", () -> writer.write("Serialize(serializer, Schema);"));
                writer.write("");
                writer.write(
                    "public override void Serialize(IShapeSerializer serializer, Schema schema)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
                      writer.write("serializer.WriteStruct(schema, new UnionMembers(this));");
                    });
                writer.write("");
                writer.write(
                    "private sealed class UnionMembers(Unknown owner) : ISerializableStruct");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write("public Schema Schema => $L.Schema;", typeName);
                      writer.write("");
                      writer.write("public void Serialize(IShapeSerializer serializer)");
                      writer.openBlock(
                          "{", "}", () -> writer.write("serializer.WriteStruct(Schema, this);"));
                      writer.write("");
                      writer.write("public void SerializeMembers(IShapeSerializer serializer)");
                      writer.openBlock(
                          "{",
                          "}",
                          () ->
                              writer.write("serializer.WriteDocument(TagSchema(), owner.Value);"));
                      writer.write("");
                      writer.write("private Schema TagSchema()");
                      writer.openBlock(
                          "{",
                          "}",
                          () ->
                              writer.write(
                                  "return Schema.CreateMember(new ShapeId(Schema.Id.Namespace,"
                                      + " Schema.Id.Name, owner.Tag), PreludeSchemas.Document);"));
                    });
              });
          writer.write("");
          writer.write("public static $L FromUnknown(string tag, Document value)", typeName);
          writer.openBlock("{", "}", () -> writer.write("return new Unknown(tag, value);"));
          writer.write("");
          writeDeserialize(typeName, members);
          writer.write("");

          // Match method
          StringBuilder header = new StringBuilder("public T Match<T>(");
          for (MemberShape m : members) {
            String pn = CSharpNaming.parameterName(m.getMemberName());
            String vt = ShapeSupport.memberTypeExpr(sp, m, false);
            header.append("System.Func<").append(vt).append(", T> ").append(pn).append(", ");
          }
          header.append("System.Func<string, Document, T> unknown)");
          writer.write(header.toString());
          writer.openBlock(
              "{",
              "}",
              () -> {
                for (MemberShape m : members) {
                  String pn = CSharpNaming.parameterName(m.getMemberName());
                  writer.write("System.ArgumentNullException.ThrowIfNull($L);", pn);
                }
                writer.write("System.ArgumentNullException.ThrowIfNull(unknown);");
                writer.write("");
                writer.write("return this switch {");
                writer.indent();
                for (MemberShape m : members) {
                  String variantName = CSharpNaming.typeName(m.getMemberName());
                  String pn = CSharpNaming.parameterName(m.getMemberName());
                  writer.write("$L value => $L(value.Value),", variantName, pn);
                }
                writer.write("Unknown value => unknown(value.Tag, value.Value),");
                writer.write(
                    "_ => throw new System.InvalidOperationException(\"Unknown union variant.\"),");
                writer.dedent();
                writer.write("};");
              });
        });
    writer.write("");
    SchemaGenerator.writeFunctionalUnionSchema(writer, context, shape, members);
  }

  private void writeDeserialize(String typeName, List<MemberShape> members) {
    Model model = context.model();
    writer.write("public static $L Deserialize(IShapeDeserializer deserializer)", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(deserializer);");
          writer.write(typeName + "? result = null;");
          writer.write("");
          writer.write(
              "deserializer.ReadStruct<object?>(Schema, null, new StructMemberConsumer<object?>(");
          writer.write("Member: (_, field, reader) =>");
          writer.openBlock(
              "{",
              "}",
              () -> {
                for (int i = 0; i < members.size(); i++) {
                  MemberShape member = members.get(i);
                  Shape target = model.expectShape(member.getTarget());
                  String memberName = member.getMemberName();
                  String variantName = CSharpNaming.typeName(memberName);
                  String keyword = i == 0 ? "if" : "else if";
                  writer.write(
                      keyword + " (field.MemberName == $L)", CSharpNaming.formatString(memberName));
                  writer.openBlock(
                      "{",
                      "}",
                      () ->
                          writer.write(
                              "result = new $L($L);",
                              variantName,
                              readValueExpression(
                                  target,
                                  "reader",
                                  SchemaGenerator.memberSchemaFieldName(member))));
                }
              });
          writer.write(",");
          writer.write("UnknownMember: (_, name, reader) =>");
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "result = new Unknown(name, reader.ReadDocument(PreludeSchemas.Document));"));
          writer.write("));");
          writer.write("");
          writer.write(
              "return result ?? throw new System.InvalidOperationException(\"Union payload was"
                  + " empty.\");");
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
      case STRUCTURE -> serializerVar + ".WriteStruct(" + schemaVar + ", " + valueExpr + ");";
      case UNION, LIST, SET, MAP ->
          valueExpr + ".Serialize(" + serializerVar + ", " + schemaVar + ");";
      default ->
          throw new IllegalArgumentException("Unsupported union member shape: " + target.getId());
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
          throw new IllegalArgumentException("Unsupported union member shape: " + target.getId());
    };
  }
}
