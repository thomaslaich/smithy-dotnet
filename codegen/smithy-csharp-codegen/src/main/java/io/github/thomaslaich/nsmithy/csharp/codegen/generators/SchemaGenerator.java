package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.AttributeEmitter;
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
        default -> throw new IllegalArgumentException("Unsupported prelude shape: " + shape.getId());
      };
    }

    SymbolProvider symbolProvider = context.symbolProvider();
    Symbol symbol = symbolProvider.toSymbol(shape);
    return CSharpSymbolProvider.qualified(symbol) + ".Schema";
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
        AttributeEmitter.shapeKindName(shape.getType()),
        shapeIdExpr(shape.getId()),
        members.stream()
            .map(SchemaGenerator::memberSchemaFieldName)
            .collect(Collectors.joining(", ")),
        traitsExpr(shape.getAllTraits().values()));
    writer.write("");
  }

  public static void writeListSchema(CSharpWriter writer, GenerationContext context, ListShape shape) {
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
        AttributeEmitter.shapeKindName(shape.getType()),
        shapeIdExpr(shape.getId()),
        traitsExpr(shape.getAllTraits().values()));
    writer.write("");
  }

  public static void writeMapSchema(CSharpWriter writer, GenerationContext context, MapShape shape) {
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
        AttributeEmitter.shapeKindName(shape.getType()),
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

  private static String memberSchemaFieldName(MemberShape member) {
    return CSharpNaming.propertyName(member.getMemberName()) + "Schema";
  }

  private static String memberTargetExpr(GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    return shapeSchemaAccessor(context, target);
  }

  private static String documentExpr(Node node) {
    return switch (node.getType()) {
      case NULL -> "Document.Null";
      case BOOLEAN -> "Document.From(" + node.expectBooleanNode().getValue() + ")";
      case STRING ->
          "Document.From(" + CSharpNaming.formatString(node.expectStringNode().getValue()) + ")";
      case NUMBER -> "Document.From((decimal)" + node.expectNumberNode().getValue() + ")";
      case ARRAY -> arrayDocumentExpr(node.expectArrayNode());
      case OBJECT -> objectDocumentExpr(node.expectObjectNode());
    };
  }

  private static String arrayDocumentExpr(ArrayNode node) {
    return "Document.From(new Document[] {"
        + node.getElements().stream().map(SchemaGenerator::documentExpr).collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectDocumentExpr(ObjectNode node) {
    return "Document.From(new System.Collections.Generic.Dictionary<string, Document>"
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
}
