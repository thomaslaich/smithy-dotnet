package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayList;
import java.util.Collection;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.traits.DeprecatedTrait;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.model.traits.EnumValueTrait;
import software.amazon.smithy.model.traits.ExamplesTrait;
import software.amazon.smithy.model.traits.ExternalDocumentationTrait;
import software.amazon.smithy.model.traits.InternalTrait;
import software.amazon.smithy.model.traits.RecommendedTrait;
import software.amazon.smithy.model.traits.SinceTrait;
import software.amazon.smithy.model.traits.SuppressTrait;
import software.amazon.smithy.model.traits.TagsTrait;
import software.amazon.smithy.model.traits.TimestampFormatTrait;
import software.amazon.smithy.model.traits.TitleTrait;
import software.amazon.smithy.model.traits.Trait;
import software.amazon.smithy.model.traits.UnstableTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * What a generated runtime schema carries, whatever language renders it: the traits that reach the
 * runtime, the members' effective traits, and the values an enum declares.
 */
@SmithyInternalApi
public final class SchemaSupport {

  private static final Set<ShapeId> BUILD_TIME_TRAITS =
      Set.of(
          DeprecatedTrait.ID,
          DocumentationTrait.ID,
          ExamplesTrait.ID,
          ExternalDocumentationTrait.ID,
          RecommendedTrait.ID,
          SinceTrait.ID,
          SuppressTrait.ID,
          TagsTrait.ID,
          TitleTrait.ID,
          UnstableTrait.ID,
          TraitIds.PROMPTS,
          ShapeId.from("smithy.rules#endpointTests"),
          ShapeId.from("smithy.waiters#waitable"));

  private static final Set<String> BUILD_TIME_TRAIT_NAMESPACES =
      Set.of("smithy.test", "aws.iam", "aws.cloudformation");

  private SchemaSupport() {}

  /**
   * Whether a trait is carried into runtime schemas. Documentation, tooling, and test traits are
   * consumed at build time and left out, which keeps schema classes small.
   */
  public static boolean isRuntimeTrait(Trait trait) {
    ShapeId id = trait.toShapeId();
    return !BUILD_TIME_TRAITS.contains(id)
        && !BUILD_TIME_TRAIT_NAMESPACES.contains(id.getNamespace());
  }

  /** The runtime traits among {@code traits}, ordered by trait id. */
  public static List<Trait> runtimeTraits(Collection<? extends Trait> traits) {
    List<Trait> runtime = new ArrayList<>();
    for (Trait trait : traits) {
      if (isRuntimeTrait(trait)) {
        runtime.add(trait);
      }
    }
    runtime.sort(Comparator.comparing(trait -> trait.toShapeId().toString()));
    return runtime;
  }

  /**
   * A member's runtime traits. A member targeting a simple shape outside the prelude also carries
   * the target's traits it does not override, since such a target has no schema of its own.
   */
  public static List<Trait> memberTraits(Model model, MemberShape member) {
    List<Trait> traits = new ArrayList<>(member.getAllTraits().values());
    Shape target = model.expectShape(member.getTarget());
    if (inlinesTargetTraits(target)) {
      for (Trait trait : target.getAllTraits().values()) {
        if (traits.stream().noneMatch(existing -> existing.toShapeId().equals(trait.toShapeId()))) {
          traits.add(trait);
        }
      }
    }
    return runtimeTraits(traits);
  }

  /** The {@code @timestampFormat} a timestamp shape carries into its schema, when it has one. */
  public static Optional<Trait> timestampFormat(Shape shape) {
    return shape.getTrait(TimestampFormatTrait.class).map(trait -> trait);
  }

  /**
   * The values an enum shape defines, so the runtime can tell a modeled value from one a peer
   * invented. Generated enum types stay open, so the schema is the only place this is recorded.
   */
  public static List<String> stringEnumValues(Shape shape) {
    return shape.asEnumShape().map(e -> List.copyOf(e.getEnumValues().values())).orElse(List.of());
  }

  /**
   * The values an enum shape marks {@code @internal}. They are valid on the wire like any other,
   * but a server leaves them out of the message it sends back when it rejects a value, so the
   * schema has to record which ones they are.
   */
  public static List<String> internalStringEnumValues(Shape shape) {
    return shape
        .asEnumShape()
        .map(
            e ->
                e.getAllMembers().values().stream()
                    .filter(m -> m.hasTrait(InternalTrait.class))
                    .map(m -> m.expectTrait(EnumValueTrait.class).expectStringValue())
                    .toList())
        .orElse(List.of());
  }

  /** The int values an intEnum shape defines. See {@link #stringEnumValues}. */
  public static List<Integer> intEnumValues(IntEnumShape shape) {
    return List.copyOf(shape.getEnumValues().values());
  }

  /**
   * Whether a shape can take part in a recursive graph, so a reference to its schema must be
   * deferred until every schema is initialized.
   */
  public static boolean isCycleCapable(ShapeType type) {
    return switch (type) {
      case STRUCTURE, UNION, LIST, SET, MAP -> true;
      default -> false;
    };
  }

  /**
   * The name of the runtime's prelude schema for a simple shape type, such as {@code Integer} for
   * {@code Schemas.Integer}, or empty for a type with no prelude schema.
   */
  public static Optional<String> preludeSchemaName(ShapeType type) {
    return Optional.ofNullable(
        switch (type) {
          case BOOLEAN -> "Boolean";
          case BYTE -> "Byte";
          case SHORT -> "Short";
          case INTEGER -> "Integer";
          case LONG -> "Long";
          case FLOAT -> "Float";
          case DOUBLE -> "Double";
          case BIG_INTEGER -> "BigInteger";
          case BIG_DECIMAL -> "BigDecimal";
          case STRING -> "String";
          case BLOB -> "Blob";
          case TIMESTAMP -> "Timestamp";
          case DOCUMENT -> "Document";
          default -> null;
        });
  }

  /**
   * The name of the runtime's schema for a shape in the {@code smithy.api} prelude, such as {@code
   * Integer} for {@code smithy.api#Integer}. The deprecated {@code Primitive*} shapes differ only
   * by a default value, which Smithy also places on every member that targets them, so they share
   * the boxed shape's schema.
   */
  public static String preludeShapeSchemaName(Shape shape) {
    return switch (shape.getId().getName()) {
      case "Boolean", "PrimitiveBoolean" -> "Boolean";
      case "Byte", "PrimitiveByte" -> "Byte";
      case "Short", "PrimitiveShort" -> "Short";
      case "Integer", "PrimitiveInteger" -> "Integer";
      case "Long", "PrimitiveLong" -> "Long";
      case "Float", "PrimitiveFloat" -> "Float";
      case "Double", "PrimitiveDouble" -> "Double";
      case "BigInteger" -> "BigInteger";
      case "BigDecimal" -> "BigDecimal";
      case "String" -> "String";
      case "Blob" -> "Blob";
      case "Timestamp" -> "Timestamp";
      case "Document" -> "Document";
      case "Unit" -> "Unit";
      default ->
          throw new CodegenException(
              "Unsupported Smithy prelude schema shape "
                  + shape.getId()
                  + " ("
                  + shape.getType()
                  + "). Supported prelude schema shapes: "
                  + SUPPORTED_PRELUDE_SHAPES
                  + ".");
    };
  }

  private static final String SUPPORTED_PRELUDE_SHAPES =
      String.join(
          ", ",
          "Boolean",
          "Byte",
          "Short",
          "Integer",
          "Long",
          "Float",
          "Double",
          "PrimitiveBoolean",
          "PrimitiveByte",
          "PrimitiveShort",
          "PrimitiveInteger",
          "PrimitiveLong",
          "PrimitiveFloat",
          "PrimitiveDouble",
          "BigInteger",
          "BigDecimal",
          "String",
          "Blob",
          "Timestamp",
          "Document",
          "Unit");

  private static boolean inlinesTargetTraits(Shape target) {
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
}
