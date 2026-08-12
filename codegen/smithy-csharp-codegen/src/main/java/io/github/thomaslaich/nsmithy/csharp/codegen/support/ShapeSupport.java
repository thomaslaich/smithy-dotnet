package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.ClientOptionalTrait;
import software.amazon.smithy.model.traits.DefaultTrait;
import software.amazon.smithy.model.traits.HttpHeaderTrait;
import software.amazon.smithy.model.traits.HttpLabelTrait;
import software.amazon.smithy.model.traits.HttpPayloadTrait;
import software.amazon.smithy.model.traits.HttpPrefixHeadersTrait;
import software.amazon.smithy.model.traits.HttpQueryParamsTrait;
import software.amazon.smithy.model.traits.HttpQueryTrait;
import software.amazon.smithy.model.traits.HttpResponseCodeTrait;
import software.amazon.smithy.model.traits.RequiredTrait;
import software.amazon.smithy.model.traits.SparseTrait;
import software.amazon.smithy.model.traits.StreamingTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ShapeSupport {

  private static final ShapeId UNIT = ShapeId.from("smithy.api#Unit");

  private ShapeSupport() {}

  /** smithy.framework#ValidationException, the error the server runtime itself returns. */
  public static final ShapeId VALIDATION_EXCEPTION =
      ShapeId.from("smithy.framework#ValidationException");

  /**
   * The runtime owns this shape: it is thrown by the server runtime when input validation fails, so
   * a model that declares it must bind to the same type rather than to a generated copy.
   */
  public static boolean isValidationException(ShapeId id) {
    return VALIDATION_EXCEPTION.equals(id);
  }

  public static boolean isUnit(ShapeId id) {
    return UNIT.equals(id);
  }

  public static boolean isRequired(MemberShape member) {
    return member.hasTrait(RequiredTrait.class);
  }

  public static boolean hasDefault(MemberShape member) {
    return member.hasTrait(DefaultTrait.class);
  }

  public static boolean hasClientOptional(MemberShape member) {
    return member.hasTrait(ClientOptionalTrait.class);
  }

  /**
   * Returns the C# literal expression for a member's @default value, or null if the member has no
   * default. Target-aware so the produced literal type matches the parameter type: * BLOB defaults
   * are base64 strings → emit {@code System.Convert.FromBase64String("…")}; * TIMESTAMP defaults
   * are epoch seconds → emit {@code System.DateTimeOffset.FromUnixTimeSeconds(N)}; * FLOAT defaults
   * need an {@code f} suffix to avoid double-to-float conversion errors; * ENUM (string-enum)
   * defaults wrap the literal in the generated {@code (string)} ctor; * INT_ENUM defaults cast the
   * integer to the generated enum type. Anything else falls through to a plain literal
   * (string/bool/numeric).
   */
  public static String defaultValueExpression(Model model, SymbolProvider sp, MemberShape member) {
    var trait = member.getTrait(DefaultTrait.class).orElse(null);
    if (trait == null) return null;
    if (hasClientOptional(member)) return null;
    var node = trait.toNode();
    Shape target = model.expectShape(member.getTarget());
    String typeName = CSharpSymbolProvider.qualified(sp.toSymbol(member));
    return literalForShape(model, sp, target, node, typeName);
  }

  private static String literalForShape(
      Model model,
      SymbolProvider sp,
      Shape target,
      software.amazon.smithy.model.node.Node node,
      String typeName) {
    if (target.getType() == ShapeType.DOCUMENT) return documentLiteral(node);
    if (node.isNullNode()) return null;
    return switch (target.getType()) {
      case BLOB ->
          node.isStringNode()
              ? "System.Convert.FromBase64String(\"" + node.expectStringNode().getValue() + "\")"
              : null;
      case TIMESTAMP ->
          node.isNumberNode()
              ? "System.DateTimeOffset.FromUnixTimeSeconds("
                  + node.expectNumberNode().getValue().longValue()
                  + ")"
              : null;
      case FLOAT ->
          node.isNumberNode() ? node.expectNumberNode().getValue().floatValue() + "f" : null;
      case ENUM ->
          node.isStringNode()
              ? "new "
                  + typeName
                  + "(\""
                  + node.expectStringNode().getValue().replace("\\", "\\\\").replace("\"", "\\\"")
                  + "\")"
              : null;
      case INT_ENUM ->
          node.isNumberNode()
              ? "(" + typeName + ")" + node.expectNumberNode().getValue().longValue()
              : null;
      case LIST, SET -> listLiteral(model, sp, target, node, typeName);
      case MAP -> mapLiteral(model, sp, target, node, typeName);
      default -> defaultLiteral(node);
    };
  }

  private static String listLiteral(
      Model model,
      SymbolProvider sp,
      Shape target,
      software.amazon.smithy.model.node.Node node,
      String typeName) {
    if (!node.isArrayNode()) return null;
    Shape memberTarget =
        model.expectShape(
            switch (target.getType()) {
              case LIST -> target.asListShape().orElseThrow().getMember().getTarget();
              case SET -> target.asSetShape().orElseThrow().getMember().getTarget();
              default ->
                  throw new CodegenException(
                      "Expected list or set shape while rendering @default for "
                          + target.getId()
                          + ", but found "
                          + target.getType()
                          + ".");
            });
    String elementType = CSharpSymbolProvider.qualified(sp.toSymbol(memberTarget));
    List<String> elements = new ArrayList<>();
    for (var element : node.expectArrayNode().getElements()) {
      String literal = literalForShape(model, sp, memberTarget, element, elementType);
      if (literal == null) return null;
      elements.add(literal);
    }
    return "new " + typeName + "(new " + elementType + "[] {" + String.join(", ", elements) + "})";
  }

  private static String mapLiteral(
      Model model,
      SymbolProvider sp,
      Shape target,
      software.amazon.smithy.model.node.Node node,
      String typeName) {
    if (!node.isObjectNode()) return null;
    var mapShape =
        target
            .asMapShape()
            .orElseThrow(
                () ->
                    new CodegenException(
                        "Expected map shape while rendering @default for "
                            + target.getId()
                            + ", but found "
                            + target.getType()
                            + "."));
    Shape keyTarget = model.expectShape(mapShape.getKey().getTarget());
    Shape valueTarget = model.expectShape(mapShape.getValue().getTarget());
    String keyType = CSharpSymbolProvider.qualified(sp.toSymbol(keyTarget));
    String valueType = CSharpSymbolProvider.qualified(sp.toSymbol(valueTarget));
    StringBuilder sb =
        new StringBuilder("new ")
            .append(typeName)
            .append("(new System.Collections.Generic.Dictionary<")
            .append(keyType)
            .append(", ")
            .append(valueType)
            .append("> {");
    boolean first = true;
    for (var entry : node.expectObjectNode().getStringMap().entrySet()) {
      String keyLiteral =
          defaultLiteral(software.amazon.smithy.model.node.Node.from(entry.getKey()));
      String valueLiteral = literalForShape(model, sp, valueTarget, entry.getValue(), valueType);
      if (keyLiteral == null || valueLiteral == null) return null;
      if (!first) sb.append(", ");
      first = false;
      sb.append("{ ").append(keyLiteral).append(", ").append(valueLiteral).append(" }");
    }
    return sb.append("})").toString();
  }

  private static String documentLiteral(software.amazon.smithy.model.node.Node node) {
    if (node.isNullNode()) return "NSmithy.Core.Document.Null";
    if (node.isBooleanNode())
      return "NSmithy.Core.Document.From(" + node.expectBooleanNode().getValue() + ")";
    if (node.isStringNode()) {
      String s = node.expectStringNode().getValue().replace("\\", "\\\\").replace("\"", "\\\"");
      return "NSmithy.Core.Document.From(\"" + s + "\")";
    }
    if (node.isNumberNode()) {
      return "NSmithy.Core.Document.From((decimal)" + node.expectNumberNode().getValue() + ")";
    }
    if (node.isArrayNode()) {
      StringBuilder sb =
          new StringBuilder("NSmithy.Core.Document.From(new NSmithy.Core.Document[] {");
      boolean first = true;
      for (var el : node.expectArrayNode().getElements()) {
        if (!first) sb.append(", ");
        first = false;
        sb.append(documentLiteral(el));
      }
      return sb.append("})").toString();
    }
    if (node.isObjectNode()) {
      StringBuilder sb =
          new StringBuilder(
              "NSmithy.Core.Document.From(new System.Collections.Generic.Dictionary<string,"
                  + " NSmithy.Core.Document> {");
      boolean first = true;
      for (var entry : node.expectObjectNode().getStringMap().entrySet()) {
        if (!first) sb.append(", ");
        first = false;
        sb.append("{ \"")
            .append(entry.getKey().replace("\\", "\\\\").replace("\"", "\\\""))
            .append("\", ")
            .append(documentLiteral(entry.getValue()))
            .append(" }");
      }
      return sb.append("})").toString();
    }
    return "NSmithy.Core.Document.Null";
  }

  private static String defaultLiteral(software.amazon.smithy.model.node.Node node) {
    if (node.isStringNode()) {
      String s = node.expectStringNode().getValue();
      return "\"" + s.replace("\\", "\\\\").replace("\"", "\\\"") + "\"";
    }
    if (node.isBooleanNode()) {
      return node.expectBooleanNode().getValue() ? "true" : "false";
    }
    if (node.isNumberNode()) {
      var num = node.expectNumberNode().getValue();
      return num.toString();
    }
    return null;
  }

  /** A member is nullable iff not @required AND either has no @default or is @clientOptional. */
  public static boolean isNullable(MemberShape member) {
    return !isRequired(member) && (!hasDefault(member) || hasClientOptional(member));
  }

  /**
   * True for C# reference types (need null-check / null-throw). Note that smithy DOCUMENT maps to
   * {@code NSmithy.Core.Document}, which is a {@code readonly record struct} (value type), so it is
   * intentionally excluded — the C# {@code ?? throw} pattern is illegal on structs.
   */
  public static boolean isReferenceType(Model model, MemberShape member) {
    Shape target = model.expectShape(member.getTarget());
    // smithy.api#Unit maps to the SmithyUnit value type, not a generated reference type.
    if (isUnit(target.getId())) {
      return false;
    }
    return switch (target.getType()) {
      case BLOB, STRING, STRUCTURE, UNION, LIST, SET, MAP -> true;
      default -> false;
    };
  }

  public static boolean usesShapeSerde(Shape target) {
    return switch (target.getType()) {
      case ENUM, STRUCTURE, UNION, LIST, SET, MAP -> true;
      default -> false;
    };
  }

  /** Members of a structure/error/union, sorted by member name. */
  public static List<MemberShape> sortedMembers(Shape shape) {
    return sortedMembers(shape, null);
  }

  public static List<MemberShape> sortedMembers(Shape shape, MemberShape excluded) {
    List<MemberShape> list = new ArrayList<>(shape.members());
    if (excluded != null) {
      list.removeIf(m -> m.getId().equals(excluded.getId()));
    }
    list.sort(Comparator.comparing(MemberShape::getMemberName));
    return list;
  }

  /** Constructor parameter ordering: required first (alphabetical), then optional. */
  public static List<MemberShape> constructorMembers(Shape shape) {
    return constructorMembers(shape, null);
  }

  public static List<MemberShape> constructorMembers(Shape shape, MemberShape excluded) {
    List<MemberShape> list = new ArrayList<>(shape.members());
    if (excluded != null) {
      list.removeIf(m -> m.getId().equals(excluded.getId()));
    }
    list.sort(
        Comparator.comparing((MemberShape m) -> isOptionalParameter(m) ? 1 : 0)
            .thenComparing(MemberShape::getMemberName));
    return list;
  }

  public static boolean isOptionalParameter(MemberShape member) {
    return isNullable(member) || hasDefault(member);
  }

  /**
   * The conventional error "message" member if present and targets smithy.api#String. The lookup is
   * case-insensitive because Smithy permits any casing for the member name and a member whose
   * camelCase parameter form would be {@code message} still collides with the always-emitted
   * base-class message parameter on the generated constructor.
   */
  public static Optional<MemberShape> errorMessageMember(Model model, StructureShape shape) {
    return shape.members().stream()
        .filter(m -> m.getMemberName().equalsIgnoreCase("message"))
        .filter(m -> model.expectShape(m.getTarget()).getType() == ShapeType.STRING)
        .findFirst();
  }

  /** Member type for property emission: type symbol + optional `?` suffix. */
  public static String memberTypeExpr(SymbolProvider sp, MemberShape member, boolean nullable) {
    Symbol s = sp.toSymbol(member);
    String base = CSharpSymbolProvider.qualified(s);
    return nullable ? base + "?" : base;
  }

  public static String memberTypeExpr(
      Model model, SymbolProvider sp, MemberShape member, boolean nullable) {
    String base;
    if (isStreamingBlobMember(model, member)) {
      base = "System.IO.Stream";
    } else if (isEventStreamMember(model, member)) {
      base =
          "System.Collections.Generic.IAsyncEnumerable<"
              + CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(member.getTarget())))
              + ">";
    } else {
      base = CSharpSymbolProvider.qualified(sp.toSymbol(member));
    }
    return nullable ? base + "?" : base;
  }

  /** Parameter type: nullable if member is nullable OR has a default. */
  public static String parameterTypeExpr(SymbolProvider sp, MemberShape member) {
    return memberTypeExpr(sp, member, isNullable(member) || hasDefault(member));
  }

  public static String parameterTypeExpr(Model model, SymbolProvider sp, MemberShape member) {
    return memberTypeExpr(model, sp, member, isNullable(member) || hasDefault(member));
  }

  public static boolean isHttpLabel(MemberShape m) {
    return m.hasTrait(HttpLabelTrait.class);
  }

  public static boolean isHttpQuery(MemberShape m) {
    return m.hasTrait(HttpQueryTrait.class);
  }

  public static boolean isHttpQueryParams(MemberShape m) {
    return m.hasTrait(HttpQueryParamsTrait.class);
  }

  public static boolean isHttpHeader(MemberShape m) {
    return m.hasTrait(HttpHeaderTrait.class);
  }

  public static boolean isHttpPrefixHeaders(MemberShape m) {
    return m.hasTrait(HttpPrefixHeadersTrait.class);
  }

  public static boolean isHttpPayload(MemberShape m) {
    return m.hasTrait(HttpPayloadTrait.class);
  }

  public static boolean isHttpResponseCode(MemberShape m) {
    return m.hasTrait(HttpResponseCodeTrait.class);
  }

  /** True if member has none of the HTTP binding traits (i.e. is part of the JSON body). */
  public static boolean isHttpBody(MemberShape m) {
    return !isHttpLabel(m)
        && !isHttpQuery(m)
        && !isHttpQueryParams(m)
        && !isHttpHeader(m)
        && !isHttpPrefixHeaders(m)
        && !isHttpPayload(m);
  }

  public static boolean isSparseTarget(Model model, MemberShape member) {
    Shape target = model.expectShape(member.getTarget());
    if (target.getType() != ShapeType.LIST
        && target.getType() != ShapeType.MAP
        && target.getType() != ShapeType.SET) {
      return false;
    }
    return target.hasTrait(SparseTrait.class);
  }

  public static boolean isSparse(Shape shape) {
    return shape.hasTrait(SparseTrait.class);
  }

  /** True when a shape itself, one of its members, or a member target carries @streaming. */
  public static boolean isStreamingShape(Model model, ShapeId shapeId) {
    Shape shape = model.expectShape(shapeId);
    if (shape.hasTrait(StreamingTrait.class)) {
      return true;
    }

    return shape.members().stream()
        .anyMatch(
            member ->
                member.hasTrait(StreamingTrait.class)
                    || model.expectShape(member.getTarget()).hasTrait(StreamingTrait.class));
  }

  /**
   * Returns the event/message shape of a streaming operation input/output. For the Smithy event
   * stream pattern this is the target of the streaming member; for direct streaming shapes this is
   * the shape itself.
   */
  public static Optional<ShapeId> streamingMemberTarget(Model model, ShapeId shapeId) {
    Shape shape = model.expectShape(shapeId);
    if (shape.hasTrait(StreamingTrait.class)) {
      return Optional.of(shapeId);
    }

    return shape.members().stream()
        .filter(
            member ->
                member.hasTrait(StreamingTrait.class)
                    || model.expectShape(member.getTarget()).hasTrait(StreamingTrait.class))
        .map(MemberShape::getTarget)
        .findFirst();
  }

  public static void requireGrpcEventStreamWrapperIsFlattenable(Model model, OperationShape op) {
    requireGrpcEventStreamWrapperIsFlattenable(model, op, op.getInputShape(), "input");
    requireGrpcEventStreamWrapperIsFlattenable(model, op, op.getOutputShape(), "output");
  }

  private static void requireGrpcEventStreamWrapperIsFlattenable(
      Model model, OperationShape op, ShapeId shapeId, String direction) {
    Shape shape = model.expectShape(shapeId);
    if (!(shape instanceof StructureShape structure) || !isEventStreamShape(model, shapeId)) {
      return;
    }

    long streamingMemberCount =
        structure.members().stream().filter(member -> isEventStreamMember(model, member)).count();
    if (streamingMemberCount == 1 && structure.members().size() == 1) {
      return;
    }

    String members =
        structure.members().stream()
            .sorted(Comparator.comparing(MemberShape::getMemberName))
            .map(
                member ->
                    member.getMemberName()
                        + (isEventStreamMember(model, member) ? " (event stream)" : ""))
            .reduce((left, right) -> left + ", " + right)
            .orElse("<none>");

    throw new CodegenException(
        "gRPC event-stream operation "
            + op.getId()
            + " "
            + direction
            + " shape "
            + shapeId
            + " must contain exactly one event-stream member. Native gRPC flattens the Smithy "
            + direction
            + " wrapper to 'stream <Event>', so sibling members cannot be represented. Members: "
            + members
            + ".");
  }

  public static boolean isStreamingBlobMember(Model model, MemberShape member) {
    Shape target = model.expectShape(member.getTarget());
    return target.getType() == ShapeType.BLOB
        && (member.hasTrait(StreamingTrait.class) || target.hasTrait(StreamingTrait.class));
  }

  public static boolean isEventStreamMember(Model model, MemberShape member) {
    Shape target = model.expectShape(member.getTarget());
    return target.getType() == ShapeType.UNION
        && (member.hasTrait(StreamingTrait.class) || target.hasTrait(StreamingTrait.class));
  }

  public static boolean isStreamingBlobShape(Model model, ShapeId shapeId) {
    Shape shape = model.expectShape(shapeId);
    if (shape.getType() == ShapeType.BLOB && shape.hasTrait(StreamingTrait.class)) {
      return true;
    }

    return shape.members().stream().anyMatch(member -> isStreamingBlobMember(model, member));
  }

  public static boolean isEventStreamShape(Model model, ShapeId shapeId) {
    return isStreamingShape(model, shapeId) && !isStreamingBlobShape(model, shapeId);
  }

  /** "Foo" -> "FooAsync". Convenience for generated method names. */
  public static String asyncMethodName(String operationName) {
    return CSharpNaming.propertyName(operationName) + "Async";
  }
}
