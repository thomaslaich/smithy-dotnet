package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.traits.Trait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class SchemaGenerator {

  private SchemaGenerator() {}

  public static void addImports(CSharpWriter writer) {
    writer.addImport(RuntimeTypes.NSMITHY_CORE);
  }

  public static String shapeSchemaAccessor(GenerationContext context, Shape shape) {
    if ("smithy.api".equals(shape.getId().getNamespace())) {
      return switch (shape.getId().getName()) {
        case "Boolean" -> "PreludeSchemas.Boolean";
        case "Byte" -> "PreludeSchemas.Byte";
        case "Short" -> "PreludeSchemas.Short";
        case "Integer" -> "PreludeSchemas.Integer";
        case "Long" -> "PreludeSchemas.Long";
        case "Float" -> "PreludeSchemas.Float";
        case "Double" -> "PreludeSchemas.Double";
        case "BigInteger" -> "PreludeSchemas.BigInteger";
        case "BigDecimal" -> "PreludeSchemas.BigDecimal";
        case "String" -> "PreludeSchemas.String";
        case "Blob" -> "PreludeSchemas.Blob";
        case "Timestamp" -> "PreludeSchemas.Timestamp";
        case "Document" -> "PreludeSchemas.Document";
        case "Unit" -> "PreludeSchemas.Unit";
        default ->
            throw new IllegalArgumentException("Unsupported prelude shape: " + shape.getId());
      };
    }

    // IntEnum generates a plain C# enum with no Schema property; its wire type is Integer.
    if (shape.getType() == ShapeType.INT_ENUM) {
      return "PreludeSchemas.Integer";
    }

    // Timestamp shapes outside smithy.api (e.g. with @timestampFormat applied) still map
    // to System.DateTimeOffset in C# which has no Schema property.
    if (shape.getType() == ShapeType.TIMESTAMP) {
      return "PreludeSchemas.Timestamp";
    }

    // Primitive shapes outside smithy.api (e.g. custom boolean/integer aliases) still map
    // to the corresponding C# primitives which have no Schema property.
    String preludeSchema = primitiveTypeToPreludeSchema(shape.getType());
    if (preludeSchema != null) {
      return preludeSchema;
    }

    SymbolProvider symbolProvider = context.symbolProvider();
    Symbol symbol = symbolProvider.toSymbol(shape);
    return CSharpSymbolProvider.qualified(symbol) + ".Schema!";
  }

  public static String shapeIdExpr(ShapeId id) {
    return "ShapeId.Parse(" + CSharpNaming.formatString(id.toString()) + ")";
  }

  public static String traitExpr(Trait trait) {
    String idExpr = shapeIdExpr(trait.toShapeId());
    Node node = trait.toNode();
    if (node.isNullNode()) {
      return "new Trait(" + idExpr + ")";
    }

    return "new Trait(" + idExpr + ", " + documentExpr(node) + ")";
  }

  public static String traitsExpr(Collection<? extends Trait> traits) {
    if (traits.isEmpty()) {
      return "null";
    }

    List<Trait> sorted = new ArrayList<>(traits);
    sorted.sort(java.util.Comparator.comparing(t -> t.toShapeId().toString()));
    return "["
        + sorted.stream().map(SchemaGenerator::traitExpr).collect(Collectors.joining(", "))
        + "]";
  }

  public static void writeStructureSchema(
      CSharpWriter writer, GenerationContext context, Shape shape, List<MemberShape> members) {
    addImports(writer);
    for (MemberShape member : members) {
      writer.write(
          "private static readonly Schema $L = Schema.CreateMember($L, () => $L, $L);",
          memberSchemaFieldName(member),
          shapeIdExpr(member.getId()),
          memberTargetExpr(context, member),
          traitsExpr(member.getAllTraits().values()));
    }
    writer.write("");
    writer.write(
        "public static Schema Schema { get; } = Schema.Create$L($L, [$L], $L);",
        shapeKindName(shape.getType()),
        shapeIdExpr(shape.getId()),
        members.stream()
            .map(SchemaGenerator::memberSchemaFieldName)
            .collect(Collectors.joining(", ")),
        traitsExpr(shape.getAllTraits().values()));
    writer.write("");
  }

  public static void writeListSchema(
      CSharpWriter writer, GenerationContext context, ListShape shape) {
    addImports(writer);
    Model model = context.model();
    Shape memberTarget = model.expectShape(shape.getMember().getTarget());
    writer.write(
        "private static readonly Schema MemberSchema = Schema.CreateMember($L, () => $L, $L);",
        shapeIdExpr(shape.getMember().getId()),
        shapeSchemaAccessor(context, memberTarget),
        traitsExpr(shape.getMember().getAllTraits().values()));
    writer.write("");
    writer.write(
        "public static Schema Schema { get; } = Schema.Create$L($L, MemberSchema, $L);",
        shapeKindName(shape.getType()),
        shapeIdExpr(shape.getId()),
        traitsExpr(shape.getAllTraits().values()));
    writer.write("");
  }

  public static void writeMapSchema(
      CSharpWriter writer, GenerationContext context, MapShape shape) {
    addImports(writer);
    Model model = context.model();
    Shape keyTarget = model.expectShape(shape.getKey().getTarget());
    Shape valueTarget = model.expectShape(shape.getValue().getTarget());
    writer.write(
        "private static readonly Schema KeySchema = Schema.CreateMember($L, () => $L, $L);",
        shapeIdExpr(shape.getKey().getId()),
        shapeSchemaAccessor(context, keyTarget),
        traitsExpr(shape.getKey().getAllTraits().values()));
    writer.write(
        "private static readonly Schema ValueSchema = Schema.CreateMember($L, () => $L, $L);",
        shapeIdExpr(shape.getValue().getId()),
        shapeSchemaAccessor(context, valueTarget),
        traitsExpr(shape.getValue().getAllTraits().values()));
    writer.write("");
    writer.write(
        "public static Schema Schema { get; } = Schema.CreateMap($L, KeySchema, ValueSchema, $L);",
        shapeIdExpr(shape.getId()),
        traitsExpr(shape.getAllTraits().values()));
    writer.write("");
  }

  public static void writeSimpleSchema(CSharpWriter writer, Shape shape) {
    addImports(writer);
    writer.write(
        "public static Schema Schema { get; } = Schema.CreateSimple($L, ShapeKind.$L, $L);",
        shapeIdExpr(shape.getId()),
        shapeKindName(shape.getType()),
        traitsExpr(shape.getAllTraits().values()));
    writer.write("");
  }

  public static String memberSchemaExpr(GenerationContext context, MemberShape member) {
    return "Schema.CreateMember("
        + shapeIdExpr(member.getId())
        + ", () => "
        + memberTargetExpr(context, member)
        + ", "
        + traitsExpr(member.getAllTraits().values())
        + ")";
  }

  public static String memberSchemaFieldName(MemberShape member) {
    return CSharpNaming.propertyName(member.getMemberName()) + "Schema";
  }

  private static String memberTargetExpr(GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    return shapeSchemaAccessor(context, target);
  }

  private static String documentExpr(Node node) {
    return switch (node.getType()) {
      case NULL -> "NSmithy.Core.Document.Null";
      case BOOLEAN -> "NSmithy.Core.Document.From(" + node.expectBooleanNode().getValue() + ")";
      case STRING ->
          "NSmithy.Core.Document.From("
              + CSharpNaming.formatString(node.expectStringNode().getValue())
              + ")";
      case NUMBER ->
          "NSmithy.Core.Document.From((decimal)" + node.expectNumberNode().getValue() + ")";
      case ARRAY -> arrayDocumentExpr(node.expectArrayNode());
      case OBJECT -> objectDocumentExpr(node.expectObjectNode());
    };
  }

  private static String arrayDocumentExpr(ArrayNode node) {
    return "NSmithy.Core.Document.From(new NSmithy.Core.Document[] {"
        + node.getElements().stream()
            .map(SchemaGenerator::documentExpr)
            .collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectDocumentExpr(ObjectNode node) {
    return "NSmithy.Core.Document.From("
        + "new System.Collections.Generic.Dictionary<string, NSmithy.Core.Document>"
        + " {"
        + node.getMembers().entrySet().stream()
            .map(SchemaGenerator::objectMemberExpr)
            .collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectMemberExpr(Map.Entry<StringNode, Node> member) {
    return "{"
        + CSharpNaming.formatString(member.getKey().getValue())
        + ", "
        + documentExpr(member.getValue())
        + "}";
  }

  /**
   * Returns the PreludeSchemas accessor for built-in primitive ShapeTypes that map to C# primitives
   * (which have no .Schema property), or null if the type is a user-defined shape.
   */
  private static String primitiveTypeToPreludeSchema(ShapeType t) {
    return switch (t) {
      case BOOLEAN -> "PreludeSchemas.Boolean";
      case BYTE -> "PreludeSchemas.Byte";
      case SHORT -> "PreludeSchemas.Short";
      case INTEGER -> "PreludeSchemas.Integer";
      case LONG -> "PreludeSchemas.Long";
      case FLOAT -> "PreludeSchemas.Float";
      case DOUBLE -> "PreludeSchemas.Double";
      case BIG_INTEGER -> "PreludeSchemas.BigInteger";
      case BIG_DECIMAL -> "PreludeSchemas.BigDecimal";
      case STRING -> "PreludeSchemas.String";
      case BLOB -> "PreludeSchemas.Blob";
      case TIMESTAMP -> "PreludeSchemas.Timestamp";
      case DOCUMENT -> "PreludeSchemas.Document";
      default -> null;
    };
  }

  private static String shapeKindName(ShapeType t) {
    return switch (t) {
      case INT_ENUM -> "IntEnum";
      default -> {
        String name = t.name().toLowerCase();
        yield Character.toUpperCase(name.charAt(0)) + name.substring(1);
      }
    };
  }
}
