package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.ClientOptionalTrait;
import software.amazon.smithy.model.traits.DefaultTrait;
import software.amazon.smithy.model.traits.DocumentationTrait;
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

  /**
   * A member's documentation, falling back to its target's when the member has none and targets a
   * list or map. Those are generated inline as {@code IReadOnlyList<T>} or {@code
   * IReadOnlyDictionary<string, T>}, so the member is the only declaration that can carry the
   * collection shape's documentation.
   */
  public static Optional<String> memberDocumentation(Model model, MemberShape member) {
    Optional<DocumentationTrait> own = member.getTrait(DocumentationTrait.class);
    if (own.isPresent()) {
      return own.map(DocumentationTrait::getValue);
    }
    Shape target = model.expectShape(member.getTarget());
    if (target.getType() != ShapeType.LIST
        && target.getType() != ShapeType.SET
        && target.getType() != ShapeType.MAP) {
      return Optional.empty();
    }
    return target.getTrait(DocumentationTrait.class).map(DocumentationTrait::getValue);
  }

  public static boolean hasClientOptional(MemberShape member) {
    return member.hasTrait(ClientOptionalTrait.class);
  }

  /** A member is nullable iff not @required AND either has no @default or is @clientOptional. */
  public static boolean isNullable(MemberShape member) {
    return !isRequired(member) && (!hasDefault(member) || hasClientOptional(member));
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
        Comparator.comparingInt((MemberShape m) -> isOptionalParameter(m) ? 1 : 0)
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

  /** Whether {@code op}'s input or output holds an event stream. */
  public static boolean isEventStreamOperation(Model model, OperationShape op) {
    return isEventStreamShape(model, op.getInputShape())
        || isEventStreamShape(model, op.getOutputShape());
  }

  private ShapeSupport() {}
}
