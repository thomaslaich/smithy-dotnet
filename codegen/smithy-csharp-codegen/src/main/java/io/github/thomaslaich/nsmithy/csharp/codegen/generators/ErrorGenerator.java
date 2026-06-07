/*
 * Renders a Smithy @error structure as a C# Exception subclass.
 * The first constructor parameter is the message (forwarded to base(message)),
 * additional members follow the same nullability conventions as a structure.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ErrorGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final StructureShape shape;

  public ErrorGenerator(GenerationContext c, CSharpWriter w, StructureShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    Optional<MemberShape> messageMember = ShapeSupport.errorMessageMember(model, shape);
    List<MemberShape> members = ShapeSupport.sortedMembers(shape);

    writer.write("public sealed partial class $L : System.Exception", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writeConstructor(typeName, messageMember.orElse(null));
          messageMember.ifPresent(
              mm -> {
                writer.write("public override string Message => base.Message!;");
                writer.write("");
              });
          writeProperties(sp, model, messageMember.orElse(null));
        });
    writer.write("");
    SchemaGenerator.writeFunctionalStructureSchema(writer, context, shape, members);
  }

  private void writeConstructor(String typeName, MemberShape messageMember) {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    List<MemberShape> ctor = ShapeSupport.constructorMembers(shape, messageMember);
    boolean hasRequired = ctor.stream().anyMatch(m -> !ShapeSupport.isOptionalParameter(m));

    StringBuilder sig = new StringBuilder("public ").append(typeName).append("(");
    sig.append("string? message");
    if (!hasRequired) sig.append(" = null");
    for (MemberShape m : ctor) {
      sig.append(", ")
          .append(ShapeSupport.parameterTypeExpr(sp, m))
          .append(' ')
          .append(CSharpNaming.parameterName(m.getMemberName()));
      if (ShapeSupport.isOptionalParameter(m)) sig.append(" = null");
    }
    sig.append(")");
    writer.write(sig.toString());
    writer.write("    : base(message)");
    if (ctor.isEmpty()) {
      writer.write("{ }");
    } else {
      writer.openBlock(
          "{",
          "}",
          () -> {
            for (MemberShape m : ctor) {
              String prop = CSharpNaming.propertyName(m.getMemberName());
              String param = CSharpNaming.parameterName(m.getMemberName());
              if (!ShapeSupport.isNullable(m) && ShapeSupport.isReferenceType(model, m)) {
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
    }
    writer.write("");
  }

  private void writeProperties(SymbolProvider sp, Model model, MemberShape excluded) {
    for (MemberShape m : ShapeSupport.sortedMembers(shape, excluded)) {
      String prop = CSharpNaming.propertyName(m.getMemberName());
      boolean nullable = ShapeSupport.isNullable(m);
      String type = ShapeSupport.memberTypeExpr(sp, m, nullable);
      writer.write("public $L $L { get; }", type, prop);
    }
  }

  private void writeSerializeMembers(
      Model model, List<MemberShape> members, MemberShape messageMember) {
    writer.write("public void SerializeMembers(IShapeSerializer serializer)");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
          for (MemberShape member : members) {
            String prop = CSharpNaming.propertyName(member.getMemberName());
            String expr = member.equals(messageMember) ? "base.Message!" : prop;
            String schema = SchemaGenerator.memberSchemaFieldName(member);
            Shape target = model.expectShape(member.getTarget());
            if (ShapeSupport.isNullable(member) && !member.equals(messageMember)) {
              String local = CSharpNaming.parameterName(member.getMemberName());
              writer.write("if ($L is { } $L)", prop, local);
              writer.openBlock(
                  "{",
                  "}",
                  () -> writer.write(writeValueStatement(target, "serializer", schema, local)));
            } else {
              writer.write(writeValueStatement(target, "serializer", schema, expr));
            }
          }
        });
  }

  private void writeDeserialize(
      String typeName,
      SymbolProvider sp,
      Model model,
      List<MemberShape> members,
      MemberShape messageMember) {
    writer.write("public static $L Deserialize(IShapeDeserializer deserializer)", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(deserializer);");
          for (MemberShape member : members) {
            writer.write(
                "$L $L = null;",
                ShapeSupport.memberTypeExpr(sp, member, true),
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
                  String local = CSharpNaming.parameterName(member.getMemberName());
                  Shape target = model.expectShape(member.getTarget());
                  String keyword = i == 0 ? "if" : "else if";
                  writer.write(
                      keyword + " (field.MemberName == $L)",
                      CSharpNaming.formatString(member.getMemberName()));
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        if (ShapeSupport.isNullable(member) && !member.equals(messageMember)) {
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
          writer.write("return new $L($L);", typeName, constructorArguments(messageMember));
        });
  }

  private String constructorArguments(MemberShape messageMember) {
    List<String> args = new java.util.ArrayList<>();
    String messageArg =
        messageMember == null ? "null" : CSharpNaming.parameterName(messageMember.getMemberName());
    args.add(messageArg);
    for (MemberShape member : ShapeSupport.constructorMembers(shape, messageMember)) {
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
          throw new IllegalArgumentException("Unsupported error member shape: " + target.getId());
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
          throw new IllegalArgumentException("Unsupported error member shape: " + target.getId());
    };
  }
}
