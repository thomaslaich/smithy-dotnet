/*
 * Renders a Smithy structure as a C# `public sealed partial record class`.
 * Constructor parameters are required-first then optional, members are
 * exposed as get-only properties, all decorated with [SmithyMember].
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
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class StructureGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final StructureShape shape;

  public StructureGenerator(GenerationContext c, CSharpWriter w, StructureShape s) {
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
        "public sealed partial record class $L : ISerializableStruct, IDeserializableShape<$L>",
        typeName,
        typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("");
          SchemaGenerator.writeStructureSchema(writer, context, shape, members);
          writer.write("Schema ISerializableShape.Schema => Schema;");
          writer.write("");
          writeConstructor(typeName);
          writeProperties(sp, model);
          writer.write("");
          writer.write("public void Serialize(IShapeSerializer serializer)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
                writer.write("serializer.WriteStruct(Schema, this);");
              });
          writer.write("");
          writeSerializeMembers(members);
          writer.write("");
          writeDeserialize(typeName, sp, model, members);
        });
  }

  private void writeConstructor(String typeName) {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    List<MemberShape> ctorMembers = ShapeSupport.constructorMembers(shape);
    if (ctorMembers.isEmpty()) {
      writer.write("public $L() { }", typeName);
      writer.write("");
      return;
    }

    StringBuilder sig = new StringBuilder("public ").append(typeName).append("(");
    for (int i = 0; i < ctorMembers.size(); i++) {
      MemberShape m = ctorMembers.get(i);
      sig.append(ShapeSupport.parameterTypeExpr(sp, m))
          .append(' ')
          .append(CSharpNaming.parameterName(m.getMemberName()));
      if (ShapeSupport.isOptionalParameter(m)) {
        sig.append(" = null");
      }
      if (i < ctorMembers.size() - 1) sig.append(", ");
    }
    sig.append(")");

    writer.write(sig.toString());
    writer.openBlock(
        "{",
        "}",
        () -> {
          for (MemberShape m : ctorMembers) {
            String prop = CSharpNaming.propertyName(m.getMemberName());
            String param = CSharpNaming.parameterName(m.getMemberName());
            String defaultExpr = ShapeSupport.defaultValueExpression(model, sp, m);
            if (defaultExpr != null) {
              writer.write("$L = $L ?? $L;", prop, param, defaultExpr);
            } else if (!ShapeSupport.isNullable(m) && ShapeSupport.isReferenceType(model, m)) {
              writer.write(
                  "$L = $L ?? throw new System.ArgumentNullException(nameof($L));",
                  prop,
                  param,
                  param);
            } else {
              writer.write("$L = $L;", prop, param);
            }
          }
        });
    writer.write("");
  }

  private void writeProperties(SymbolProvider sp, Model model) {
    for (MemberShape m : ShapeSupport.sortedMembers(shape)) {
      String prop = CSharpNaming.propertyName(m.getMemberName());
      boolean nullable = ShapeSupport.isNullable(m);
      String type = ShapeSupport.memberTypeExpr(sp, m, nullable);
      writer.write("public $L $L { get; }", type, prop);
    }
  }

  private void writeSerializeMembers(List<MemberShape> members) {
    Model model = context.model();
    writer.write("public void SerializeMembers(IShapeSerializer serializer)");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
          for (MemberShape member : members) {
            String prop = CSharpNaming.propertyName(member.getMemberName());
            String schema = SchemaGenerator.memberSchemaFieldName(member);
            Shape target = model.expectShape(member.getTarget());
            if (ShapeSupport.isNullable(member)) {
              String local = CSharpNaming.parameterName(member.getMemberName());
              writer.write("if ($L is { } $L)", prop, local);
              writer.openBlock(
                  "{",
                  "}",
                  () -> writer.write(writeValueStatement(target, "serializer", schema, local)));
            } else {
              writer.write(writeValueStatement(target, "serializer", schema, prop));
            }
          }
        });
  }

  private void writeDeserialize(
      String typeName, SymbolProvider sp, Model model, List<MemberShape> members) {
    writer.write("public static $L Deserialize(IShapeDeserializer deserializer)", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(deserializer);");
          for (MemberShape member : members) {
            writer.write(
                "$L $L = null;",
                deserializationLocalType(sp, member),
                CSharpNaming.parameterName(member.getMemberName()));
          }
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
                  String memberName = member.getMemberName();
                  String local = CSharpNaming.parameterName(memberName);
                  Shape target = model.expectShape(member.getTarget());
                  String keyword = i == 0 ? "if" : "else if";
                  writer.write(
                      keyword + " (field.MemberName == $L)", CSharpNaming.formatString(memberName));
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        if (ShapeSupport.isNullable(member)) {
                          writer.write("if (reader.IsNull())");
                          writer.openBlock("{", "}", () -> writer.write("reader.ReadNull();"));
                          writer.write("else");
                          writer.openBlock(
                              "{",
                              "}",
                              () ->
                                  writer.write(
                                      local
                                          + " = "
                                          + readValueExpression(
                                              target,
                                              "reader",
                                              SchemaGenerator.memberSchemaFieldName(member))
                                          + ";"));
                        } else {
                          writer.write(
                              local
                                  + " = "
                                  + readValueExpression(
                                      target,
                                      "reader",
                                      SchemaGenerator.memberSchemaFieldName(member))
                                  + ";");
                        }
                      });
                }
              });
          writer.write("));");
          writer.write("");
          writer.write("return new $L($L);", typeName, constructorArguments());
        });
  }

  private String constructorArguments() {
    List<String> args = new java.util.ArrayList<>();
    for (MemberShape member : ShapeSupport.constructorMembers(shape)) {
      String local = CSharpNaming.parameterName(member.getMemberName());
      if (ShapeSupport.isOptionalParameter(member)) {
        args.add(local);
      } else {
        args.add(
            local
                + " ?? throw new System.InvalidOperationException("
                + CSharpNaming.formatString(
                    "Missing required member '" + member.getMemberName() + "'.")
                + ")");
      }
    }
    return String.join(", ", args);
  }

  private String deserializationLocalType(SymbolProvider sp, MemberShape member) {
    return ShapeSupport.memberTypeExpr(sp, member, true);
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
          throw new IllegalArgumentException(
              "Unsupported structure member shape: " + target.getId());
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
          throw new IllegalArgumentException(
              "Unsupported structure member shape: " + target.getId());
    };
  }
}
