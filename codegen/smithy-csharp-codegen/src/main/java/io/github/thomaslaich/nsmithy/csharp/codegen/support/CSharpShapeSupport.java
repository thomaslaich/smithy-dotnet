package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import io.github.thomaslaich.nsmithy.codegen.core.Naming;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.List;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.traits.DefaultTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/** Shape helpers that render C#: type expressions, literals, and C#'s reference-type rules. */
@SmithyInternalApi
public final class CSharpShapeSupport {

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
  public static String defaultValueExpression(
      CSharpWriter writer, Model model, SymbolProvider sp, MemberShape member) {
    var trait = member.getTrait(DefaultTrait.class).orElse(null);
    if (trait == null) return null;
    if (ShapeSupport.hasClientOptional(member)) return null;
    var node = trait.toNode();
    Shape target = model.expectShape(member.getTarget());
    String typeName = writer.typeName(sp.toSymbol(member));
    return literalForShape(writer, model, sp, target, node, typeName);
  }

  /**
   * A C# {@code decimal} literal for a model number. A cast such as {@code
   * (decimal)-9223372036854775809} does not compile, since C# reads the operand as a {@code ulong}
   * before negating it; an {@code m}-suffixed literal is a single constant.
   */
  public static String decimalLiteral(Number value) {
    return new java.math.BigDecimal(value.toString()).toPlainString() + "m";
  }

  public static String documentLiteral(
      CSharpWriter writer, software.amazon.smithy.model.node.Node node) {
    if (node.isNullNode()) return writer.typeName(RuntimeTypes.DOCUMENT) + ".Null";
    if (node.isBooleanNode())
      return writer.typeName(RuntimeTypes.DOCUMENT)
          + ".From("
          + node.expectBooleanNode().getValue()
          + ")";
    if (node.isStringNode()) {
      String s = node.expectStringNode().getValue().replace("\\", "\\\\").replace("\"", "\\\"");
      return writer.typeName(RuntimeTypes.DOCUMENT) + ".From(\"" + s + "\")";
    }
    if (node.isNumberNode()) {
      return writer.typeName(RuntimeTypes.DOCUMENT)
          + ".From("
          + decimalLiteral(node.expectNumberNode().getValue())
          + ")";
    }
    if (node.isArrayNode()) {
      StringBuilder sb =
          new StringBuilder(
              writer.typeName(RuntimeTypes.DOCUMENT)
                  + ".From(new "
                  + writer.typeName(RuntimeTypes.DOCUMENT)
                  + "[] {");
      boolean first = true;
      for (var el : node.expectArrayNode().getElements()) {
        if (!first) sb.append(", ");
        first = false;
        sb.append(documentLiteral(writer, el));
      }
      return sb.append("})").toString();
    }
    if (node.isObjectNode()) {
      StringBuilder sb =
          new StringBuilder(
              writer.typeName(RuntimeTypes.DOCUMENT)
                  + ".From(new "
                  + writer.typeName(RuntimeTypes.DICTIONARY)
                  + "<string,"
                  + " "
                  + writer.typeName(RuntimeTypes.DOCUMENT)
                  + "> {");
      boolean first = true;
      for (var entry : node.expectObjectNode().getStringMap().entrySet()) {
        if (!first) sb.append(", ");
        first = false;
        sb.append("{ \"")
            .append(entry.getKey().replace("\\", "\\\\").replace("\"", "\\\""))
            .append("\", ")
            .append(documentLiteral(writer, entry.getValue()))
            .append(" }");
      }
      return sb.append("})").toString();
    }
    return writer.typeName(RuntimeTypes.DOCUMENT) + ".Null";
  }

  /**
   * True for C# reference types (need null-check / null-throw). Note that smithy DOCUMENT maps to
   * {@code NSmithy.Core.Document}, which is a {@code readonly record struct} (value type), so it is
   * intentionally excluded — the C# {@code ?? throw} pattern is illegal on structs.
   */
  public static boolean isReferenceType(Model model, MemberShape member) {
    Shape target = model.expectShape(member.getTarget());
    // smithy.api#Unit maps to the SmithyUnit value type, not a generated reference type.
    if (ShapeSupport.isUnit(target.getId())) {
      return false;
    }
    return switch (target.getType()) {
      case BLOB, STRING, STRUCTURE, UNION, LIST, SET, MAP -> true;
      default -> false;
    };
  }

  /** Member type for property emission: type symbol + optional `?` suffix. */
  public static String memberTypeExpr(
      CSharpWriter writer, SymbolProvider sp, MemberShape member, boolean nullable) {
    Symbol s = sp.toSymbol(member);
    String base = writer.typeName(s);
    return nullable ? base + "?" : base;
  }

  public static String memberTypeExpr(
      CSharpWriter writer, Model model, SymbolProvider sp, MemberShape member, boolean nullable) {
    String base;
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      base = writer.typeName(RuntimeTypes.STREAM);
    } else if (ShapeSupport.isEventStreamMember(model, member)) {
      base =
          writer.typeName(RuntimeTypes.I_ASYNC_ENUMERABLE)
              + "<"
              + writer.typeName(sp.toSymbol(model.expectShape(member.getTarget())))
              + ">";
    } else {
      base = writer.typeName(sp.toSymbol(member));
    }
    return nullable ? base + "?" : base;
  }

  /** Parameter type: nullable if member is nullable OR has a default. */
  public static String parameterTypeExpr(
      CSharpWriter writer, SymbolProvider sp, MemberShape member) {
    return memberTypeExpr(
        writer, sp, member, ShapeSupport.isNullable(member) || ShapeSupport.hasDefault(member));
  }

  public static String parameterTypeExpr(
      CSharpWriter writer, Model model, SymbolProvider sp, MemberShape member) {
    return memberTypeExpr(
        writer,
        model,
        sp,
        member,
        ShapeSupport.isNullable(member) || ShapeSupport.hasDefault(member));
  }

  /** "Foo" -> "FooAsync". Convenience for generated method names. */
  public static String asyncMethodName(String operationName) {
    return Naming.pascal(operationName) + "Async";
  }

  private static String literalForShape(
      CSharpWriter writer,
      Model model,
      SymbolProvider sp,
      Shape target,
      software.amazon.smithy.model.node.Node node,
      String typeName) {
    if (target.getType() == ShapeType.DOCUMENT) return documentLiteral(writer, node);
    if (node.isNullNode()) return null;
    return switch (target.getType()) {
      case BLOB ->
          node.isStringNode()
              ? writer.typeName(RuntimeTypes.CONVERT)
                  + ".FromBase64String(\""
                  + node.expectStringNode().getValue()
                  + "\")"
              : null;
      case TIMESTAMP ->
          node.isNumberNode()
              ? writer.typeName(RuntimeTypes.DATE_TIME_OFFSET)
                  + ".FromUnixTimeSeconds("
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
      case LIST, SET -> listLiteral(writer, model, sp, target, node);
      case MAP -> mapLiteral(writer, model, sp, target, node);
      default -> defaultLiteral(node);
    };
  }

  private static String listLiteral(
      CSharpWriter writer,
      Model model,
      SymbolProvider sp,
      Shape target,
      software.amazon.smithy.model.node.Node node) {
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
    String elementType = writer.typeName(sp.toSymbol(memberTarget));
    List<String> elements = new ArrayList<>();
    for (var element : node.expectArrayNode().getElements()) {
      String literal = literalForShape(writer, model, sp, memberTarget, element, elementType);
      if (literal == null) return null;
      elements.add(literal);
    }
    return "new " + elementType + "[] {" + String.join(", ", elements) + "}";
  }

  private static String mapLiteral(
      CSharpWriter writer,
      Model model,
      SymbolProvider sp,
      Shape target,
      software.amazon.smithy.model.node.Node node) {
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
    Shape valueTarget = model.expectShape(mapShape.getValue().getTarget());
    String valueType = writer.typeName(sp.toSymbol(valueTarget));
    StringBuilder sb =
        new StringBuilder("new ")
            .append(writer.typeName(RuntimeTypes.DICTIONARY))
            .append("<string, ")
            .append(valueType)
            .append("> {");
    boolean first = true;
    for (var entry : node.expectObjectNode().getStringMap().entrySet()) {
      String keyLiteral =
          defaultLiteral(software.amazon.smithy.model.node.Node.from(entry.getKey()));
      String valueLiteral =
          literalForShape(writer, model, sp, valueTarget, entry.getValue(), valueType);
      if (keyLiteral == null || valueLiteral == null) return null;
      if (!first) sb.append(", ");
      first = false;
      sb.append("{ ").append(keyLiteral).append(", ").append(valueLiteral).append(" }");
    }
    return sb.append("}").toString();
  }

  private static String defaultLiteral(software.amazon.smithy.model.node.Node node) {
    if (node.isStringNode()) {
      return CSharpNaming.formatString(node.expectStringNode().getValue());
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

  private CSharpShapeSupport() {}
}
