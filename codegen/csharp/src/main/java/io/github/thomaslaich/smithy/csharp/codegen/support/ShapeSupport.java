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
   * no default or the default is the type's zero value (which we don't need to materialize).
   * Currently supports string, boolean, and numeric defaults — sufficient for the simpleRestJson
   * conformance fixtures.
   */
  public static String defaultValueExpression(MemberShape member) {
    var trait = member.getTrait(DefaultTrait.class).orElse(null);
    if (trait == null) return null;
    var node = trait.toNode();
    if (node.isNullNode()) return null;
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
    // Arrays / objects (e.g. empty list/map defaults) not yet supported here.
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

  /** The "message" member if present and targets smithy.api#String. */
  public static Optional<MemberShape> errorMessageMember(StructureShape shape) {
    return shape
        .getMember("message")
        .filter(m -> m.getTarget().toString().equals("smithy.api#String"));
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
