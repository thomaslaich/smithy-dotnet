package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.SymbolProperties;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.model.traits.EnumValueTrait;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.model.traits.InternalTrait;
import software.amazon.smithy.model.traits.Trait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class SchemaGenerator {

  public static String shapeSchemaAccessor(
      CSharpWriter writer, GenerationContext context, Shape shape) {
    if ("smithy.api".equals(shape.getId().getNamespace())) {
      return switch (shape.getId().getName()) {
        case "Boolean" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Boolean");
        case "Byte" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Byte");
        case "Short" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Short");
        case "Integer" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Integer");
        case "Long" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Long");
        case "Float" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Float");
        case "Double" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Double");
        case "BigInteger" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".BigInteger");
        case "BigDecimal" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".BigDecimal");
        case "String" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".String");
        case "Blob" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Blob");
        case "Timestamp" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Timestamp");
        case "Document" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Document");
        case "Unit" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Unit");
        default ->
            throw new CodegenException(
                "Unsupported Smithy prelude schema shape "
                    + shape.getId()
                    + " ("
                    + shape.getType()
                    + "). Supported prelude schema shapes: "
                    + supportedPreludeSchemaShapeNames()
                    + ".");
      };
    }

    if (shape.getType() == ShapeType.TIMESTAMP) {
      // Carry @timestampFormat into the schema so codecs resolve the wire format from it
      // (covers struct members, list elements, and map values uniformly).
      List<Trait> tsTraits =
          shape.getAllTraits().values().stream()
              .filter(t -> t.toShapeId().toString().equals("smithy.api#timestampFormat"))
              .collect(Collectors.toList());
      return tsTraits.isEmpty()
          ? (writer.typeName(RuntimeTypes.SCHEMAS) + ".Timestamp")
          : (writer.typeName(RuntimeTypes.SCHEMAS) + ".TimestampWithTraits(")
              + traitsExpr(writer, tsTraits)
              + ")";
    }

    String preludeSchema =
        shape.getType() == ShapeType.ENUM
            ? null
            : primitiveTypeToPreludeSchema(writer, shape.getType());
    if (preludeSchema != null) {
      return preludeSchema;
    }

    String accessor = schemaClassName(writer, context, shape) + ".Schema";

    // Aggregate shapes can participate in recursive graphs (a shape referencing itself
    // directly or through a cycle). A direct static reference would observe null while the
    // referenced schema's static initializer is still running, so defer it lazily. The
    // null-forgiving '!' suppresses the nullable-flow warning for self-references where the
    // property is not yet definitely assigned at the point the lambda is created.
    return isCycleCapable(shape.getType())
        ? (writer.typeName(RuntimeTypes.SCHEMAS) + ".Lazy(() => ") + accessor + "!)"
        : accessor;
  }

  public static String schemaClassName(
      CSharpWriter writer, GenerationContext context, Shape shape) {
    return writer.typeName(context.symbolProvider().toSymbol(shape), "Schema");
  }

  public static String operationSchemaAccessor(
      CSharpWriter writer, GenerationContext context, OperationShape shape) {
    return writer.typeName(context.symbolProvider().toSymbol(shape), "Schema") + ".Schema";
  }

  public static String serviceSchemaAccessor(
      CSharpWriter writer,
      GenerationContext context,
      software.amazon.smithy.model.shapes.ServiceShape service) {
    return writer.typeName(context.symbolProvider().toSymbol(service), "Schema") + ".Schema";
  }

  public static String operationShapeType(
      CSharpWriter writer, GenerationContext context, ShapeId id) {
    if (ShapeSupport.isUnit(id)) return writer.typeName(RuntimeTypes.SMITHY_UNIT);
    return writer.typeName(context.symbolProvider().toSymbol(context.model().expectShape(id)));
  }

  public static String operationShapeSchema(
      CSharpWriter writer, GenerationContext context, ShapeId id) {
    if (ShapeSupport.isUnit(id)) return (writer.typeName(RuntimeTypes.SCHEMAS) + ".Unit");
    return shapeSchemaAccessor(writer, context, context.model().expectShape(id));
  }

  public static String shapeIdExpr(CSharpWriter writer, ShapeId id) {
    return (writer.typeName(RuntimeTypes.SHAPE_ID) + ".Parse(")
        + CSharpNaming.formatString(id.toString())
        + ")";
  }

  public static String traitExpr(CSharpWriter writer, Trait trait) {
    String idExpr = shapeIdExpr(writer, trait.toShapeId());
    Node node = trait.toNode();
    if (node.isNullNode()) {
      return ("new " + writer.typeName(RuntimeTypes.TRAIT) + "(") + idExpr + ")";
    }

    return ("new " + writer.typeName(RuntimeTypes.TRAIT) + "(")
        + idExpr
        + ", "
        + documentExpr(writer, node)
        + ")";
  }

  public static String traitsExpr(CSharpWriter writer, Collection<? extends Trait> traits) {
    if (traits.isEmpty()) {
      return "null";
    }

    List<Trait> sorted = new ArrayList<>(traits);
    sorted.sort(java.util.Comparator.comparing(t -> t.toShapeId().toString()));
    return "["
        + sorted.stream().map(value -> traitExpr(writer, value)).collect(Collectors.joining(", "))
        + "]";
  }

  public static void writeStructureSchema(
      CSharpWriter writer, GenerationContext context, Shape shape, List<MemberShape> members) {
    SymbolProvider sp = context.symbolProvider();
    String typeName = writer.typeName(sp.toSymbol(shape));

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public sealed class Builder");
          writer.openBlock(
              "{",
              "}",
              () -> {
                for (MemberShape member : members) {
                  writer.write(
                      "public $L $L { get; set; }",
                      ShapeSupport.memberTypeExpr(writer, context.model(), sp, member, true),
                      CSharpNaming.propertyName(member.getMemberName()));
                }
              });
          writer.write("");
          writer.write(
              "private sealed class ValueSerializer : $T<$L>",
              RuntimeTypes.I_STRUCT_VALUE_SERIALIZER,
              typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "public void WriteMembers<TWriter>($L value, ref TWriter writer)", typeName);
                writer.write("    where TWriter : struct, $T", RuntimeTypes.I_STRUCT_MEMBER_WRITER);
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      for (int index = 0; index < members.size(); index++) {
                        MemberShape member = members.get(index);
                        writer.write(
                            "writer.WriteMember<$L>($L, value.$L);",
                            ShapeSupport.memberTypeExpr(writer, context.model(), sp, member, true),
                            index,
                            CSharpNaming.propertyName(member.getMemberName()));
                      }
                    });
              });
          writer.write("");
          writer.write("public static $T<$L> Schema { get; } =", RuntimeTypes.SCHEMA, typeName);
          writer.indent();
          writer.write(
              "$T.Structure<$L, Builder>($L, $L)",
              RuntimeTypes.SCHEMAS,
              typeName,
              shapeIdExpr(writer, shape.getId()),
              traitsExpr(writer, shape.getAllTraits().values()));
          writer.indent();
          for (MemberShape member : members) {
            String method = ShapeSupport.isRequired(member) ? "Required" : "Optional";
            String name = member.getMemberName();
            String prop = CSharpNaming.propertyName(name);
            writer.write(".$L(", method);
            writer.indent();
            writer.write("$L,", CSharpNaming.formatString(name));
            writer.write("static value => value.$L,", prop);
            writer.write("static (builder, value) => builder.$L = value,", prop);
            writer.write("$L,", memberTargetExpr(writer, context, member));
            writer.write("$L)", memberTraitsExpr(writer, context, member));
            writer.dedent();
          }
          writer.write(".Build(");
          writer.indent();
          writer.write("static () => new Builder(),");
          writer.write(
              "static builder => new $L($L),",
              typeName,
              constructorArguments(writer, context, shape, members));
          writer.write("new ValueSerializer())");
          writer.dedent();
          writer.write(";");
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  public static void writeListSchema(
      CSharpWriter writer, GenerationContext context, ListShape shape) {
    SymbolProvider sp = context.symbolProvider();
    Shape memberTarget = context.model().expectShape(shape.getMember().getTarget());
    String typeName = writer.typeName(sp.toSymbol(shape));
    boolean sparse = ShapeSupport.isSparse(shape);
    String memberType = writer.typeName(sp.toSymbol(memberTarget)) + (sparse ? "?" : "");
    String builderType = writer.typeName(RuntimeTypes.LIST) + "<" + memberType + ">";
    String factory = shape.getType() == ShapeType.SET ? "Set" : "List";
    String elementSchema =
        elementSchemaExpr(writer, context, shape.getMember(), memberTarget, sparse);

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static $T<$L> Schema { get; } =", RuntimeTypes.SCHEMA, typeName);
          writer.indent();
          writer.write(
              "$T.$L<$L, $L, $L>($L, $L,",
              RuntimeTypes.SCHEMAS,
              factory,
              typeName,
              memberType,
              builderType,
              shapeIdExpr(writer, shape.getId()),
              elementSchema);
          writer.indent();
          writer.write("static value => value.Values,");
          writer.write("static () => new $L(),", builderType);
          writer.write("static (builder, value) => builder.Add(value),");
          writer.write("static builder => $L.FromOwnedList(builder),", typeName);
          writer.write(
              "$L, elementTraits: $L);",
              traitsExpr(writer, shape.getAllTraits().values()),
              memberTraitsExpr(writer, context, shape.getMember()));
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  public static void writeMapSchema(
      CSharpWriter writer, GenerationContext context, MapShape shape) {
    SymbolProvider sp = context.symbolProvider();
    Shape valueTarget = context.model().expectShape(shape.getValue().getTarget());
    String typeName = writer.typeName(sp.toSymbol(shape));
    boolean sparse = ShapeSupport.isSparse(shape);
    String valueType = writer.typeName(sp.toSymbol(valueTarget)) + (sparse ? "?" : "");
    String builderType = writer.typeName(RuntimeTypes.DICTIONARY) + "<string, " + valueType + ">";
    String valueSchema = elementSchemaExpr(writer, context, shape.getValue(), valueTarget, sparse);

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static $T<$L> Schema { get; } =", RuntimeTypes.SCHEMA, typeName);
          writer.indent();
          writer.write(
              "$T.Map<$L, $L, $L>($L, $L,",
              RuntimeTypes.SCHEMAS,
              typeName,
              valueType,
              builderType,
              shapeIdExpr(writer, shape.getId()),
              valueSchema);
          writer.indent();
          writer.write("static value => value.Values,");
          writer.write(
              "static () => new $L($T.Ordinal),", builderType, RuntimeTypes.STRING_COMPARER);
          writer.write("static (builder, key, value) => builder[key] = value,");
          writer.write("static builder => $L.FromOwnedDictionary(builder),", typeName);
          writer.write(
              "$L, keyTraits: $L, valueTraits: $L, key: $L);",
              traitsExpr(writer, shape.getAllTraits().values()),
              memberTraitsExpr(writer, context, shape.getKey()),
              memberTraitsExpr(writer, context, shape.getValue()),
              shapeSchemaAccessor(
                  writer, context, context.model().expectShape(shape.getKey().getTarget())));
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  public static void writeSimpleSchema(CSharpWriter writer, Shape shape) {
    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (shape.getType() == ShapeType.ENUM) {
            String typeName = CSharpNaming.typeName(shape.getId().getName());
            writer.write(
                "public static $T<$L> Schema { get; } = $T.StringEnum<$L>($L, values: $L, traits:"
                    + " $L, internalValues: $L);",
                RuntimeTypes.SCHEMA,
                typeName,
                RuntimeTypes.SCHEMAS,
                typeName,
                shapeIdExpr(writer, shape.getId()),
                stringEnumValuesExpr(shape),
                traitsExpr(writer, shape.getAllTraits().values()),
                stringEnumInternalValuesExpr(shape));
            writer.write("");
            return;
          }

          String prelude = primitiveTypeToPreludeSchema(writer, shape.getType());
          if (prelude == null) {
            prelude = (writer.typeName(RuntimeTypes.SCHEMAS) + ".String");
          }
          writer.write(
              "public static $T<$L> Schema { get; } = $L;",
              RuntimeTypes.SCHEMA,
              CSharpNaming.typeName(shape.getId().getName()),
              prelude);
          writer.write("");
        });
  }

  public static void writeUnionSchema(
      CSharpWriter writer, GenerationContext context, UnionShape shape, List<MemberShape> members) {
    String typeName = writer.typeName(context.symbolProvider().toSymbol(shape));

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static $T<$L> Schema { get; } =", RuntimeTypes.SCHEMA, typeName);
          writer.indent();
          writer.write(
              "$T.Union<$L>($L, $L)",
              RuntimeTypes.SCHEMAS,
              typeName,
              shapeIdExpr(writer, shape.getId()),
              traitsExpr(writer, shape.getAllTraits().values()));
          writer.indent();
          for (MemberShape member : members) {
            String variantName = CSharpNaming.typeName(member.getMemberName());
            writer.write(".Case(");
            writer.indent();
            writer.write("$L,", CSharpNaming.formatString(member.getMemberName()));
            writer.write("static value => value is $L.$L,", typeName, variantName);
            writer.write("static value => (($L.$L)value).Value,", typeName, variantName);
            writer.write("static value => new $L.$L(value!),", typeName, variantName);
            writer.write("$L,", rawMemberTargetExpr(writer, context, member));
            writer.write("$L)", memberTraitsExpr(writer, context, member));
            writer.dedent();
          }
          writer.write(".Build();");
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  /**
   * The values an enum shape defines, so the runtime can tell a modeled value from one a peer
   * invented. Generated enum types stay open, so the schema is the only place this is recorded.
   */
  public static String stringEnumValuesExpr(Shape shape) {
    return shape
        .asEnumShape()
        .map(
            e ->
                e.getEnumValues().values().stream()
                    .map(CSharpNaming::formatString)
                    .collect(Collectors.joining(", ", "[", "]")))
        .orElse("null");
  }

  /**
   * The values an enum shape marks {@code @internal}. They are valid on the wire like any other,
   * but a server leaves them out of the message it sends back when it rejects a value, so the
   * schema has to record which ones they are.
   */
  public static String stringEnumInternalValuesExpr(Shape shape) {
    return shape
        .asEnumShape()
        .map(
            e ->
                e.getAllMembers().values().stream()
                    .filter(m -> m.hasTrait(InternalTrait.class))
                    .map(m -> m.expectTrait(EnumValueTrait.class).expectStringValue())
                    .map(CSharpNaming::formatString)
                    .collect(Collectors.toList()))
        .filter(values -> !values.isEmpty())
        .map(values -> String.join(", ", values))
        .map(values -> "[" + values + "]")
        .orElse("null");
  }

  /** The int values an intEnum shape defines. See {@link #stringEnumValuesExpr}. */
  public static String intEnumValuesExpr(IntEnumShape shape) {
    return shape.getEnumValues().values().stream()
        .map(String::valueOf)
        .collect(Collectors.joining(", ", "[", "]"));
  }

  private SchemaGenerator() {}

  private static boolean isCycleCapable(ShapeType type) {
    return switch (type) {
      case STRUCTURE, UNION, LIST, SET, MAP -> true;
      default -> false;
    };
  }

  private static String supportedPreludeSchemaShapeNames() {
    return String.join(
        ", ",
        "Boolean",
        "Byte",
        "Short",
        "Integer",
        "Long",
        "Float",
        "Double",
        "BigInteger",
        "BigDecimal",
        "String",
        "Blob",
        "Timestamp",
        "Document",
        "Unit");
  }

  private static String localSchemaClassName(Shape shape) {
    return CSharpNaming.typeName(shape.getId().getName()) + "Schema";
  }

  private static String memberTargetExpr(
      CSharpWriter writer, GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    String targetExpr = targetSchemaExpr(writer, context, member, target);
    String nullableMemberType =
        ShapeSupport.memberTypeExpr(
            writer, context.model(), context.symbolProvider(), member, true);
    if (!nullableMemberType.endsWith("?")) {
      return targetExpr;
    }

    if (!ShapeSupport.isReferenceType(context.model(), member)) {
      return (writer.typeName(RuntimeTypes.SCHEMAS) + ".Nullable(") + targetExpr + ")";
    }
    return (writer.typeName(RuntimeTypes.SCHEMAS) + ".NullableReference(") + targetExpr + ")";
  }

  private static String rawMemberTargetExpr(
      CSharpWriter writer, GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    return targetSchemaExpr(writer, context, member, target);
  }

  private static String targetSchemaExpr(
      CSharpWriter writer, GenerationContext context, MemberShape member, Shape target) {
    if (ShapeSupport.isStreamingBlobMember(context.model(), member)) {
      return (writer.typeName(RuntimeTypes.SCHEMAS) + ".StreamingBlob");
    }
    if (ShapeSupport.isEventStreamMember(context.model(), member)) {
      return (writer.typeName(RuntimeTypes.SCHEMAS) + ".EventStream(")
          + shapeSchemaAccessor(writer, context, target)
          + ")";
    }
    return shapeSchemaAccessor(writer, context, target);
  }

  /**
   * Element/value schema accessor for a list or map member, wrapped in a nullable schema when the
   * enclosing collection is {@code @sparse}. Sparse collections carry nullable elements/values in
   * the generated model type, so the schema's element type must match.
   */
  private static String elementSchemaExpr(
      CSharpWriter writer,
      GenerationContext context,
      MemberShape member,
      Shape target,
      boolean sparse) {
    String targetExpr = shapeSchemaAccessor(writer, context, target);
    String base;
    if (!sparse) {
      base = targetExpr;
    } else {
      base =
          ShapeSupport.isReferenceType(context.model(), member)
              ? (writer.typeName(RuntimeTypes.SCHEMAS) + ".NullableReference(") + targetExpr + ")"
              : (writer.typeName(RuntimeTypes.SCHEMAS) + ".Nullable(") + targetExpr + ")";
    }

    return base;
  }

  private static String constructorArguments(
      CSharpWriter writer, GenerationContext context, Shape shape, List<MemberShape> members) {
    if (!shape.isStructureShape()) {
      return members.stream()
          .map(m -> "builder." + CSharpNaming.propertyName(m.getMemberName()))
          .collect(Collectors.joining(", "));
    }

    List<String> args = new ArrayList<>();
    MemberShape errorMessageMember = null;
    if (shape.hasTrait(ErrorTrait.class) && shape.isStructureShape()) {
      errorMessageMember =
          ShapeSupport.errorMessageMember(context.model(), shape.asStructureShape().orElseThrow())
              .orElse(null);
      args.add(
          errorMessageMember == null
              ? "null"
              : "builder." + CSharpNaming.propertyName(errorMessageMember.getMemberName()));
    }
    for (MemberShape member :
        ShapeSupport.constructorMembers(shape.asStructureShape().orElseThrow())) {
      if (errorMessageMember != null && member.equals(errorMessageMember)) {
        continue;
      }
      String prop = CSharpNaming.propertyName(member.getMemberName());
      String expr = "builder." + prop;
      if (ShapeSupport.isRequired(member)) {
        Symbol memberSymbol = context.symbolProvider().toSymbol(member);
        boolean memberIsValueType =
            memberSymbol.getProperty(SymbolProperties.IS_VALUE_TYPE, Boolean.class).orElse(false);
        if (memberIsValueType) {
          expr = "builder." + prop + ".GetValueOrDefault()";
        } else {
          // Typed so the server runtime can tell a caller's missing member from a server fault and
          // answer with smithy.framework#ValidationException instead of a 500.
          expr =
              "builder."
                  + prop
                  + (" ?? throw new "
                      + writer.typeName(RuntimeTypes.MISSING_REQUIRED_MEMBER_EXCEPTION)
                      + "(")
                  + CSharpNaming.formatString(member.getMemberName())
                  + ")";
        }
      }
      args.add(expr);
    }
    return String.join(", ", args);
  }

  private static String memberTraitsExpr(
      CSharpWriter writer, GenerationContext context, MemberShape member) {
    List<Trait> traits = new ArrayList<>(member.getAllTraits().values());
    Shape target = context.model().expectShape(member.getTarget());
    if (shouldInlineTargetTraits(target)) {
      for (Trait trait : target.getAllTraits().values()) {
        if (traits.stream().noneMatch(existing -> existing.toShapeId().equals(trait.toShapeId()))) {
          traits.add(trait);
        }
      }
    }
    return traitsExpr(writer, traits);
  }

  private static boolean shouldInlineTargetTraits(Shape target) {
    if ("smithy.api".equals(target.getId().getNamespace())) {
      return false;
    }

    return switch (target.getType()) {
      case BOOLEAN,
          BYTE,
          SHORT,
          INTEGER,
          LONG,
          FLOAT,
          DOUBLE,
          BIG_INTEGER,
          BIG_DECIMAL,
          STRING,
          BLOB,
          TIMESTAMP,
          DOCUMENT ->
          true;
      default -> false;
    };
  }

  private static String documentExpr(CSharpWriter writer, Node node) {
    return switch (node.getType()) {
      case NULL -> (writer.typeName(RuntimeTypes.DOCUMENT) + ".Null");
      case BOOLEAN ->
          (writer.typeName(RuntimeTypes.DOCUMENT) + ".From(")
              + node.expectBooleanNode().getValue()
              + ")";
      case STRING ->
          (writer.typeName(RuntimeTypes.DOCUMENT) + ".From(")
              + CSharpNaming.formatString(node.expectStringNode().getValue())
              + ")";
      case NUMBER ->
          (writer.typeName(RuntimeTypes.DOCUMENT) + ".From((decimal)")
              + node.expectNumberNode().getValue()
              + ")";
      case ARRAY -> arrayDocumentExpr(writer, node.expectArrayNode());
      case OBJECT -> objectDocumentExpr(writer, node.expectObjectNode());
    };
  }

  private static String arrayDocumentExpr(CSharpWriter writer, ArrayNode node) {
    return (writer.typeName(RuntimeTypes.DOCUMENT)
            + ".From(new "
            + writer.typeName(RuntimeTypes.DOCUMENT)
            + "[] {")
        + node.getElements().stream()
            .map(value -> documentExpr(writer, value))
            .collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectDocumentExpr(CSharpWriter writer, ObjectNode node) {
    return (writer.typeName(RuntimeTypes.DOCUMENT) + ".From(")
        + "new "
        + writer.typeName(RuntimeTypes.DICTIONARY)
        + ("<string, " + writer.typeName(RuntimeTypes.DOCUMENT) + ">")
        + " {"
        + node.getMembers().entrySet().stream()
            .map(value -> objectMemberExpr(writer, value))
            .collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectMemberExpr(CSharpWriter writer, Map.Entry<StringNode, Node> member) {
    return "{"
        + CSharpNaming.formatString(member.getKey().getValue())
        + ", "
        + documentExpr(writer, member.getValue())
        + "}";
  }

  private static String primitiveTypeToPreludeSchema(CSharpWriter writer, ShapeType t) {
    return switch (t) {
      case BOOLEAN -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Boolean");
      case BYTE -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Byte");
      case SHORT -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Short");
      case INTEGER -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Integer");
      case LONG -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Long");
      case FLOAT -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Float");
      case DOUBLE -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Double");
      case BIG_INTEGER -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".BigInteger");
      case BIG_DECIMAL -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".BigDecimal");
      case STRING -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".String");
      case BLOB -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Blob");
      case TIMESTAMP -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Timestamp");
      case DOCUMENT -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Document");
      default -> null;
    };
  }
}
