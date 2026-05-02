/*
 * Shared shape/member utilities used by all generators. Mirrors the inline
 * helpers in NSmithy.CodeGeneration.CSharp.CSharpShapeGenerator.cs.
 */
package io.github.thomaslaich.smithy.csharp.codegen.support;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.CSharpSymbolProvider;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
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
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ShapeSupport {

  private ShapeSupport() {}

  public static boolean isRequired(MemberShape member) {
    return member.hasTrait(RequiredTrait.class);
  }

  public static boolean hasDefault(MemberShape member) {
    return member.hasTrait(DefaultTrait.class);
  }

  /**
   * Returns the C# literal expression for a member's @default value, or null if the member has
   * no default. Target-aware so the produced literal type matches the parameter type:
   *   * BLOB defaults are base64 strings → emit {@code System.Convert.FromBase64String("…")};
   *   * TIMESTAMP defaults are epoch seconds → emit
   *     {@code System.DateTimeOffset.FromUnixTimeSeconds(N)};
   *   * FLOAT defaults need an {@code f} suffix to avoid double-to-float conversion errors;
   *   * ENUM (string-enum) defaults wrap the literal in the generated {@code (string)} ctor;
   *   * INT_ENUM defaults cast the integer to the generated enum type.
   * Anything else falls through to a plain literal (string/bool/numeric).
   */
  public static String defaultValueExpression(Model model, SymbolProvider sp, MemberShape member) {
    var trait = member.getTrait(DefaultTrait.class).orElse(null);
    if (trait == null) return null;
    var node = trait.toNode();
    Shape target = model.expectShape(member.getTarget());
    // Document defaults can legally be the JSON literal null — that maps to Document.Null
    // (the document type's "absent" sentinel value), not to a missing default. Handle this
    // first so the generic null-check below doesn't drop the trait.
    if (target.getType() == ShapeType.DOCUMENT) {
      return documentLiteral(node);
    }
    if (node.isNullNode()) return null;
    String typeName = CSharpSymbolProvider.qualified(sp.toSymbol(member));
    return switch (target.getType()) {
      case BLOB -> node.isStringNode()
          ? "System.Convert.FromBase64String(\"" + node.expectStringNode().getValue() + "\")"
          : null;
      case TIMESTAMP -> node.isNumberNode()
          ? "System.DateTimeOffset.FromUnixTimeSeconds("
              + node.expectNumberNode().getValue().longValue()
              + ")"
          : null;
      case FLOAT -> node.isNumberNode()
          ? node.expectNumberNode().getValue().floatValue() + "f"
          : null;
      case ENUM -> node.isStringNode()
          ? "new " + typeName + "(\""
              + node.expectStringNode().getValue().replace("\\", "\\\\").replace("\"", "\\\"")
              + "\")"
          : null;
      case INT_ENUM -> node.isNumberNode()
          ? "(" + typeName + ")" + node.expectNumberNode().getValue().longValue()
          : null;
      default -> defaultLiteral(node);
    };
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
      StringBuilder sb = new StringBuilder("NSmithy.Core.Document.From(new NSmithy.Core.Document[] {");
      boolean first = true;
      for (var el : node.expectArrayNode().getElements()) {
        if (!first) sb.append(", ");
        first = false;
        sb.append(documentLiteral(el));
      }
      return sb.append("})").toString();
    }
    if (node.isObjectNode()) {
      StringBuilder sb = new StringBuilder(
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

  /** A member is nullable iff not @required AND has no @default. */
  public static boolean isNullable(MemberShape member) {
    return !isRequired(member) && !hasDefault(member);
  }

  /**
   * True for C# reference types (need null-check / null-throw). Note that smithy DOCUMENT maps to
   * {@code NSmithy.Core.Document}, which is a {@code readonly record struct} (value type), so it is
   * intentionally excluded — the C# {@code ?? throw} pattern is illegal on structs.
   */
  public static boolean isReferenceType(Model model, MemberShape member) {
    Shape target = model.expectShape(member.getTarget());
    return switch (target.getType()) {
      case BLOB, STRING, STRUCTURE, UNION, LIST, SET, MAP -> true;
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
   * The conventional error "message" member if present and targets smithy.api#String. The
   * lookup is case-insensitive because Smithy permits any casing for the member name and a
   * member whose camelCase parameter form would be {@code message} still collides with the
   * always-emitted base-class message parameter on the generated constructor.
   */
  public static Optional<MemberShape> errorMessageMember(StructureShape shape) {
    return shape.members().stream()
        .filter(m -> m.getMemberName().equalsIgnoreCase("message"))
        .filter(m -> m.getTarget().toString().equals("smithy.api#String"))
        .findFirst();
  }

  /** Member type for property emission: type symbol + optional `?` suffix. */
  public static String memberTypeExpr(SymbolProvider sp, MemberShape member, boolean nullable) {
    Symbol s = sp.toSymbol(member);
    String base = CSharpSymbolProvider.qualified(s);
    return nullable ? base + "?" : base;
  }

  /** Parameter type: nullable if member is nullable OR has a default. */
  public static String parameterTypeExpr(SymbolProvider sp, MemberShape member) {
    return memberTypeExpr(sp, member, isNullable(member) || hasDefault(member));
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

  /** "Foo" -> "FooAsync". Convenience for generated method names. */
  public static String asyncMethodName(String operationName) {
    return CSharpNaming.propertyName(operationName) + "Async";
  }
}
