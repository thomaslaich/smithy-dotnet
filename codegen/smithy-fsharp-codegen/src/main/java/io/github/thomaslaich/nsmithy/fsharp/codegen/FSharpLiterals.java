package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.SchemaSupport;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.math.BigDecimal;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.Collection;
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.DefaultTrait;
import software.amazon.smithy.model.traits.Trait;
import software.amazon.smithy.utils.SmithyInternalApi;

/** F# expressions for model values: member defaults, documents, shape ids, and traits. */
@SmithyInternalApi
final class FSharpLiterals {

  static final String DOCUMENT = "global.NSmithy.Core.Document";

  private FSharpLiterals() {}

  /** The expression for a member's modeled default. Only called for members that use it. */
  static String defaultValue(FSharpContext context, MemberShape member) {
    Model model = context.model();
    Node node = member.expectTrait(DefaultTrait.class).toNode();
    Shape target = model.expectShape(member.getTarget());
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      return "global.System.IO.Stream.Null";
    }
    return switch (target.getType()) {
      case BOOLEAN -> String.valueOf(node.expectBooleanNode().getValue());
      case BYTE -> signed(number(node) + "y");
      case SHORT -> signed(number(node) + "s");
      case INTEGER -> signed(number(node));
      case LONG -> signed(number(node) + "L");
      case FLOAT -> signed(fractional(node) + "f");
      case DOUBLE -> signed(fractional(node));
      case BIG_DECIMAL -> signed(number(node) + "M");
      case BIG_INTEGER -> signed(number(node) + "I");
      case STRING -> FSharpNaming.stringLiteral(node.expectStringNode().getValue());
      case ENUM ->
          context.symbolProvider().typeOf(target)
              + ".FromValue("
              + FSharpNaming.stringLiteral(node.expectStringNode().getValue())
              + ")";
      case INT_ENUM ->
          "global.Microsoft.FSharp.Core.LanguagePrimitives.EnumOfValue<int, "
              + context.symbolProvider().typeOf(target)
              + ">("
              + number(node)
              + ")";
      case BLOB -> {
        String base64 = node.expectStringNode().getValue();
        yield base64.isEmpty()
            ? "[||]"
            : "global.System.Convert.FromBase64String(" + FSharpNaming.stringLiteral(base64) + ")";
      }
      case TIMESTAMP -> timestamp(node);
      case DOCUMENT -> document(node);
      case LIST, SET -> "[]";
      case MAP -> "global.Microsoft.FSharp.Collections.Map.empty";
      default ->
          throw new IllegalStateException(
              "No F# default for " + member.getId() + " targeting " + target.getType());
    };
  }

  static String document(Node node) {
    return switch (node.getType()) {
      case NULL -> DOCUMENT + ".Null";
      case BOOLEAN -> DOCUMENT + ".From(" + node.expectBooleanNode().getValue() + ")";
      case STRING ->
          DOCUMENT
              + ".From("
              + FSharpNaming.stringLiteral(node.expectStringNode().getValue())
              + ")";
      case NUMBER ->
          DOCUMENT
              + ".From("
              + new BigDecimal(node.expectNumberNode().getValue().toString()).toPlainString()
              + "M)";
      case ARRAY ->
          DOCUMENT
              + ".From(("
              + node.expectArrayNode().getElements().stream()
                  .map(FSharpLiterals::document)
                  .collect(Collectors.joining("; ", "[| ", " |]"))
                  .replace("[|  |]", "[||]")
              + ": "
              + DOCUMENT
              + "[]))";
      case OBJECT -> {
        String entries =
            node.expectObjectNode().getStringMap().entrySet().stream()
                .map(
                    e ->
                        "("
                            + FSharpNaming.stringLiteral(e.getKey())
                            + ", "
                            + document(e.getValue())
                            + ")")
                .collect(Collectors.joining("; "));
        String dictionary =
            "global.System.Collections.Generic.Dictionary<string, " + DOCUMENT + ">";
        yield DOCUMENT
            + ".From("
            + dictionary
            + (entries.isEmpty() ? "()" : "(dict [ " + entries + " ])")
            + ")";
      }
    };
  }

  static String shapeId(ShapeId id) {
    return "global.NSmithy.Core.ShapeId.Parse(" + FSharpNaming.stringLiteral(id.toString()) + ")";
  }

  static String trait(Trait trait) {
    Node node = trait.toNode();
    String id = shapeId(trait.toShapeId());
    return node.isNullNode()
        ? "global.NSmithy.Core.Trait(" + id + ")"
        : "global.NSmithy.Core.Trait(" + id + ", " + document(node) + ")";
  }

  /** An array of the runtime traits among {@code traits}, or null when there are none. */
  static String traits(Collection<? extends Trait> traits) {
    List<Trait> runtime = SchemaSupport.runtimeTraits(traits);
    if (runtime.isEmpty()) {
      return null;
    }
    return runtime.stream()
        .map(FSharpLiterals::trait)
        .collect(Collectors.joining("; ", "[| ", " |]"));
  }

  /** An F# array literal of string literals. */
  static String strings(List<String> values) {
    return values.isEmpty()
        ? "([||]: string[])"
        : values.stream()
            .map(FSharpNaming::stringLiteral)
            .collect(Collectors.joining("; ", "[| ", " |]"));
  }

  private static String number(Node node) {
    return new BigDecimal(node.expectNumberNode().getValue().toString())
        .stripTrailingZeros()
        .toPlainString();
  }

  /** A floating-point literal, which F# requires to have a decimal point. */
  private static String fractional(Node node) {
    String value = number(node);
    return value.contains(".") ? value : value + ".0";
  }

  /** A negative literal in parentheses, so it is never read as a subtraction. */
  private static String signed(String literal) {
    return literal.startsWith("-") ? "(" + literal + ")" : literal;
  }

  /** A timestamp default is epoch seconds, possibly fractional, or a date-time string. */
  private static String timestamp(Node node) {
    BigDecimal seconds =
        node.isNumberNode()
            ? new BigDecimal(node.expectNumberNode().getValue().toString())
            : epochSeconds(node.expectStringNode().getValue());
    if (seconds.stripTrailingZeros().scale() <= 0) {
      return "global.System.DateTimeOffset.FromUnixTimeSeconds("
          + signed(seconds.toBigInteger() + "L")
          + ")";
    }
    return "global.System.DateTimeOffset.UnixEpoch.AddTicks("
        + signed(seconds.movePointRight(7).longValue() + "L")
        + ")";
  }

  private static BigDecimal epochSeconds(String dateTime) {
    Instant instant = OffsetDateTime.parse(dateTime).toInstant();
    return BigDecimal.valueOf(instant.getEpochSecond())
        .add(BigDecimal.valueOf(instant.getNano(), 9));
  }
}
