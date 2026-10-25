package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.traits.DefaultTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * How a structure or error member appears in F#. A member is an {@code option} when the model lets
 * it be absent: not {@code @required}, and with no {@code @default} a client can rely on. A member
 * with such a default is a plain value, which reading fills in with the default when the input
 * leaves it out.
 */
@SmithyInternalApi
final class FSharpMembers {

  private FSharpMembers() {}

  static boolean isOptional(MemberShape member) {
    return ShapeSupport.isNullable(member)
        || (!ShapeSupport.isRequired(member) && hasNullDefault(member));
  }

  /** {@code @default(null)} removes a default the target would otherwise give the member. */
  private static boolean hasNullDefault(MemberShape member) {
    return member
        .getTrait(DefaultTrait.class)
        .map(trait -> trait.toNode().isNullNode())
        .orElse(false);
  }

  /** Whether the member has a default that applies when the input leaves it out. */
  static boolean usesDefault(MemberShape member) {
    return !isOptional(member)
        && !ShapeSupport.isRequired(member)
        && ShapeSupport.hasDefault(member);
  }

  /** The member's type without {@code option}. */
  static String valueType(FSharpContext context, MemberShape member) {
    Model model = context.model();
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      return "global.System.IO.Stream";
    }
    if (ShapeSupport.isEventStreamMember(model, member)) {
      return "global.System.Collections.Generic.IAsyncEnumerable<"
          + context.symbolProvider().typeOf(model.expectShape(member.getTarget()))
          + ">";
    }
    return context.symbolProvider().typeOf(model.expectShape(member.getTarget()));
  }

  /** The member's field or property type. */
  static String fieldType(FSharpContext context, MemberShape member) {
    String type = valueType(context, member);
    return isOptional(member) ? type + " option" : type;
  }

  /**
   * Whether the member's value is a .NET value type, which a deserializer cannot hand back as null
   * and so is checked for an explicit null before it is read.
   */
  static boolean isValueType(Model model, MemberShape member) {
    if (ShapeSupport.isStreamingBlobMember(model, member)
        || ShapeSupport.isEventStreamMember(model, member)) {
      return false;
    }
    Shape target = model.expectShape(member.getTarget());
    if (ShapeSupport.isUnit(target.getId())) {
      return true;
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
          TIMESTAMP,
          DOCUMENT,
          INT_ENUM ->
          true;
      default -> false;
    };
  }
}
