/*
 * Conversion expression helpers for round-tripping between the Smithy model
 * shapes and protoc-generated gRPC types.
 *
 * For the moment we emit a *simple* projection: the gRPC type is constructed
 * via property assignment from the Smithy structure (and vice versa). This
 * mirrors `GetSmithyToGrpcValueExpression` / `GetGrpcToSmithyValueExpression`
 * in the original .NET ServerEmitter for the most common cases (scalar
 * primitives, structures, lists, maps). More involved conversions (unions,
 * sparse maps, enums) can be added as needed.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import java.util.Optional;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.SparseTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class GrpcConversions {

  private static final ShapeId PROTO_NUM_TYPE = ShapeId.from("alloy.proto#protoNumType");
  private static final ShapeId PROTO_INLINED_ONE_OF = ShapeId.from("alloy.proto#protoInlinedOneOf");

  private GrpcConversions() {}

  /**
   * Build a gRPC message instance from a Smithy structure value. {@code src} is the C# expression
   * yielding the Smithy struct (e.g. "input" or "output.Foo").
   */
  public static String smithyToGrpc(
      SymbolProvider sp, Model model, Shape shape, String src, String grpcNs) {
    if (shape.getType() == ShapeType.UNION) {
      return smithyUnionToGrpcMessage(sp, model, shape, src, grpcNs);
    }
    if (!(shape instanceof StructureShape s)) return src;
    String grpcType = "global::" + grpcNs + "." + CSharpNaming.typeName(s.getId().getName());
    StringBuilder sb =
        new StringBuilder("((System.Func<")
            .append(grpcType)
            .append(">)(() => { var message = new ")
            .append(grpcType)
            .append("(); ");
    for (MemberShape m : ShapeSupport.sortedMembers(s)) {
      String propAccess = src + "." + CSharpNaming.propertyName(m.getMemberName());
      appendSmithyToGrpcAssignment(sb, sp, model, m, propAccess, grpcNs);
    }
    sb.append("return message; }))()");
    return sb.toString();
  }

  private static void appendSmithyToGrpcAssignment(
      StringBuilder sb,
      SymbolProvider sp,
      Model model,
      MemberShape m,
      String propAccess,
      String grpcNs) {
    String prop = CSharpNaming.propertyName(m.getMemberName());
    Shape target = model.expectShape(m.getTarget());

    if (target.getType() == ShapeType.UNION && target.hasTrait(PROTO_INLINED_ONE_OF)) {
      if (ShapeSupport.isNullable(m)) {
        String local = CSharpNaming.parameterName(m.getMemberName()) + "Value";
        sb.append("if (").append(propAccess).append(" is { } ").append(local).append(") { ");
        appendSmithyUnionToGrpcOneofAssignments(sb, sp, model, target, local, grpcNs);
        sb.append("} ");
      } else {
        appendSmithyUnionToGrpcOneofAssignments(sb, sp, model, target, propAccess, grpcNs);
      }
      return;
    }

    if (ShapeSupport.isNullable(m)) {
      String local = CSharpNaming.parameterName(m.getMemberName()) + "Value";
      sb.append("if (").append(propAccess).append(" is { } ").append(local).append(") { ");
      if (target.getType() == ShapeType.LIST || target.getType() == ShapeType.SET) {
        sb.append("message.")
            .append(prop)
            .append(".AddRange(")
            .append(smithyToGrpcMemberExpr(sp, model, m, local, grpcNs))
            .append("); ");
      } else if (target.getType() == ShapeType.MAP) {
        sb.append("message.")
            .append(prop)
            .append(".Add(")
            .append(smithyToGrpcMemberExpr(sp, model, m, local, grpcNs))
            .append("); ");
      } else {
        sb.append("message.")
            .append(prop)
            .append(" = ")
            .append(smithyToGrpcMemberExpr(sp, model, m, local, grpcNs))
            .append("; ");
      }
      sb.append("} ");
      return;
    }

    if (target.getType() == ShapeType.LIST || target.getType() == ShapeType.SET) {
      sb.append("message.")
          .append(prop)
          .append(".AddRange(")
          .append(smithyToGrpcMemberExpr(sp, model, m, propAccess, grpcNs))
          .append("); ");
    } else if (target.getType() == ShapeType.MAP) {
      sb.append("message.")
          .append(prop)
          .append(".Add(")
          .append(smithyToGrpcMemberExpr(sp, model, m, propAccess, grpcNs))
          .append("); ");
    } else {
      sb.append("message.")
          .append(prop)
          .append(" = ")
          .append(smithyToGrpcMemberExpr(sp, model, m, propAccess, grpcNs))
          .append("; ");
    }
  }

  /**
   * Build a Smithy structure value from a gRPC message. {@code src} is the C# expression yielding
   * the gRPC message (e.g. "response").
   */
  public static String grpcToSmithy(
      SymbolProvider sp, Model model, Shape shape, String src, String grpcNs) {
    if (shape.getType() == ShapeType.UNION) {
      return grpcUnionMessageToSmithy(sp, model, shape, src, grpcNs);
    }
    if (!(shape instanceof StructureShape s)) return src;
    String smithyType =
        io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider.qualified(sp.toSymbol(s));
    StringBuilder sb = new StringBuilder("new ").append(smithyType).append("(");
    var members = ShapeSupport.constructorMembers(s);
    for (int i = 0; i < members.size(); i++) {
      if (i > 0) sb.append(", ");
      MemberShape m = members.get(i);
      String propAccess = src + "." + CSharpNaming.propertyName(m.getMemberName());
      String converted = grpcToSmithyMemberExpr(sp, model, s, m, src, propAccess, grpcNs);
      if (isProtoOptionalScalar(model, m)) {
        String prop = CSharpNaming.propertyName(m.getMemberName());
        converted = src + ".Has" + prop + " ? " + converted + " : null";
      }
      sb.append(converted);
    }
    sb.append(")");
    return sb.toString();
  }

  private static String smithyToGrpcMemberExpr(
      SymbolProvider sp, Model model, MemberShape m, String expr, String grpcNs) {
    Shape target = model.expectShape(m.getTarget());
    return smithyToGrpcValueExpr(sp, model, target, m, expr, grpcNs);
  }

  private static String smithyToGrpcValueExpr(
      SymbolProvider sp,
      Model model,
      Shape target,
      MemberShape member,
      String expr,
      String grpcNs) {
    switch (target.getType()) {
      case STRUCTURE:
        return nullGuard(member, expr, smithyToGrpc(sp, model, target, expr, grpcNs));
      case UNION:
        return smithyUnionToGrpcOneof(sp, model, target, expr, grpcNs);
      case LIST:
      case SET:
        ListShape list = (ListShape) target;
        Shape listMemberTarget = model.expectShape(list.getMember().getTarget());
        return "System.Linq.Enumerable.Select("
            + expr
            + ".Values, value => "
            + (ShapeSupport.isSparse(list)
                ? smithyToGrpcValueExpr(
                    sp, model, listMemberTarget, list.getMember(), "value", grpcNs)
                : smithyToGrpcNonNullableValueExpr(
                    sp, model, listMemberTarget, list.getMember(), "value", grpcNs))
            + ")";
      case MAP:
        MapShape map = (MapShape) target;
        Shape valueTarget = model.expectShape(map.getValue().getTarget());
        if (map.hasTrait(SparseTrait.class)) {
          return "System.Linq.Enumerable.ToDictionary("
              + expr
              + ".Values, entry => entry.Key, entry => entry.Value is null ?"
              + " global::Google.Protobuf.WellKnownTypes.Value.ForNull() : "
              + smithyToProtoValueExpr(valueTarget, "entry.Value")
              + ")";
        }
        return "System.Linq.Enumerable.ToDictionary("
            + expr
            + ".Values, entry => entry.Key, entry => "
            + smithyToGrpcValueExpr(sp, model, valueTarget, map.getValue(), "entry.Value", grpcNs)
            + ")";
      case TIMESTAMP:
        return "global::Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(" + expr + ")";
      case ENUM:
        return smithyStringEnumToGrpc((EnumShape) target, expr, grpcNs);
      case INT_ENUM:
        return "(global::"
            + grpcNs
            + "."
            + CSharpNaming.typeName(target.getId().getName())
            + ")(int)"
            + expr;
      case BIG_INTEGER:
      case BIG_DECIMAL:
        return expr + ".ToString(System.Globalization.CultureInfo.InvariantCulture)";
      case DOCUMENT:
        throw new IllegalArgumentException(
            "gRPC document conversion is not implemented for: " + target.getId());
      default:
        return smithyToGrpcScalarExpr(model, member, expr);
    }
  }

  private static String smithyToGrpcScalarExpr(Model model, MemberShape m, String expr) {
    Optional<String> numType =
        m.findTrait(PROTO_NUM_TYPE).map(t -> t.toNode().expectStringNode().getValue());
    if (numType.isPresent()) {
      String nt = numType.get();
      if ("UNSIGNED".equals(nt) || "FIXED".equals(nt)) {
        Shape target = model.expectShape(m.getTarget());
        boolean isLong = target.getType() == ShapeType.LONG;
        boolean nullable = ShapeSupport.isNullable(m) && expr.contains(".");
        String innerExpr = nullable ? "(" + expr + " ?? 0)" : expr;
        return "(" + (isLong ? "ulong" : "uint") + ")" + innerExpr;
      }
    }
    return expr;
  }

  private static String grpcToSmithyMemberExpr(
      SymbolProvider sp,
      Model model,
      StructureShape parent,
      MemberShape m,
      String src,
      String expr,
      String grpcNs) {
    Shape target = model.expectShape(m.getTarget());
    if (target.getType() == ShapeType.UNION && target.hasTrait(PROTO_INLINED_ONE_OF)) {
      return grpcOneofToSmithyUnion(sp, model, parent, m, target, src, grpcNs);
    }
    return grpcToSmithyValueExpr(sp, model, target, m, expr, grpcNs);
  }

  private static String grpcToSmithyValueExpr(
      SymbolProvider sp,
      Model model,
      Shape target,
      MemberShape member,
      String expr,
      String grpcNs) {
    switch (target.getType()) {
      case STRUCTURE:
        return nullGuard(member, expr, grpcToSmithy(sp, model, target, expr, grpcNs));
      case UNION:
        throw new IllegalArgumentException(
            "Non-inlined gRPC union conversion is not implemented for: " + target.getId());
      case LIST:
      case SET:
        ListShape list = (ListShape) target;
        Shape listMemberTarget = model.expectShape(list.getMember().getTarget());
        String listType = CSharpSymbolProvider.qualified(sp.toSymbol(target));
        return "new "
            + listType
            + "("
            + "System.Linq.Enumerable.Select("
            + expr
            + ", value => "
            + (ShapeSupport.isSparse(list)
                ? grpcToSmithyValueExpr(
                    sp, model, listMemberTarget, list.getMember(), "value", grpcNs)
                : grpcToSmithyNonNullableValueExpr(
                    sp, model, listMemberTarget, list.getMember(), "value", grpcNs))
            + "))";
      case MAP:
        MapShape map = (MapShape) target;
        Shape valueTarget = model.expectShape(map.getValue().getTarget());
        String mapType = CSharpSymbolProvider.qualified(sp.toSymbol(target));
        if (map.hasTrait(SparseTrait.class)) {
          return "new "
              + mapType
              + "("
              + "System.Linq.Enumerable.ToDictionary("
              + expr
              + ", entry => entry.Key, entry => entry.Value.KindCase =="
              + " global::Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue ? null : "
              + protoValueToSmithyExpr(valueTarget, "entry.Value")
              + "))";
        }
        return "new "
            + mapType
            + "("
            + "System.Linq.Enumerable.ToDictionary("
            + expr
            + ", entry => entry.Key, entry => "
            + grpcToSmithyValueExpr(sp, model, valueTarget, map.getValue(), "entry.Value", grpcNs)
            + "))";
      case TIMESTAMP:
        return expr + ".ToDateTimeOffset()";
      case ENUM:
        return grpcStringEnumToSmithy(sp, (EnumShape) target, expr);
      case INT_ENUM:
        return "(" + CSharpSymbolProvider.qualified(sp.toSymbol(target)) + ")(int)" + expr;
      case BIG_INTEGER:
        return "System.Numerics.BigInteger.Parse("
            + expr
            + ", System.Globalization.CultureInfo.InvariantCulture)";
      case BIG_DECIMAL:
        return "decimal.Parse(" + expr + ", System.Globalization.CultureInfo.InvariantCulture)";
      case DOCUMENT:
        throw new IllegalArgumentException(
            "gRPC document conversion is not implemented for: " + target.getId());
      default:
        return grpcToSmithyScalarExpr(model, member, expr);
    }
  }

  private static String grpcToSmithyScalarExpr(Model model, MemberShape m, String expr) {
    Optional<String> numType =
        m.findTrait(PROTO_NUM_TYPE).map(t -> t.toNode().expectStringNode().getValue());
    if (numType.isPresent()) {
      String nt = numType.get();
      if ("UNSIGNED".equals(nt) || "FIXED".equals(nt)) {
        Shape target = model.expectShape(m.getTarget());
        boolean isLong = target.getType() == ShapeType.LONG;
        boolean nullable = ShapeSupport.isNullable(m);
        String baseType = isLong ? "long" : "int";
        String castType = nullable ? "(" + baseType + "?)" : "(" + baseType + ")";
        return castType + expr;
      }
    }
    return expr;
  }

  private static String smithyToGrpcNonNullableValueExpr(
      SymbolProvider sp,
      Model model,
      Shape target,
      MemberShape member,
      String expr,
      String grpcNs) {
    if (target.getType() == ShapeType.STRUCTURE) {
      return smithyToGrpc(sp, model, target, expr, grpcNs);
    }
    return smithyToGrpcValueExpr(sp, model, target, member, expr, grpcNs);
  }

  private static String grpcToSmithyNonNullableValueExpr(
      SymbolProvider sp,
      Model model,
      Shape target,
      MemberShape member,
      String expr,
      String grpcNs) {
    if (target.getType() == ShapeType.STRUCTURE) {
      return grpcToSmithy(sp, model, target, expr, grpcNs);
    }
    return grpcToSmithyValueExpr(sp, model, target, member, expr, grpcNs);
  }

  private static String smithyToProtoValueExpr(Shape target, String expr) {
    return switch (target.getType()) {
      case STRING -> "global::Google.Protobuf.WellKnownTypes.Value.ForString(" + expr + ")";
      case BOOLEAN -> "global::Google.Protobuf.WellKnownTypes.Value.ForBool(" + expr + ")";
      case BYTE, SHORT, INTEGER, LONG, FLOAT, DOUBLE ->
          "global::Google.Protobuf.WellKnownTypes.Value.ForNumber(" + expr + ")";
      default ->
          throw new IllegalArgumentException(
              "Unsupported sparse map protobuf Value target: " + target.getId());
    };
  }

  private static String protoValueToSmithyExpr(Shape target, String expr) {
    return switch (target.getType()) {
      case STRING -> expr + ".StringValue";
      case BOOLEAN -> expr + ".BoolValue";
      case BYTE -> "(sbyte)" + expr + ".NumberValue";
      case SHORT -> "(short)" + expr + ".NumberValue";
      case INTEGER -> "(int)" + expr + ".NumberValue";
      case LONG -> "(long)" + expr + ".NumberValue";
      case FLOAT -> "(float)" + expr + ".NumberValue";
      case DOUBLE -> expr + ".NumberValue";
      default ->
          throw new IllegalArgumentException(
              "Unsupported sparse map protobuf Value target: " + target.getId());
    };
  }

  private static String smithyStringEnumToGrpc(EnumShape target, String expr, String grpcNs) {
    String grpcType = "global::" + grpcNs + "." + CSharpNaming.typeName(target.getId().getName());
    StringBuilder sb = new StringBuilder(expr).append(".Value switch { ");
    boolean first = true;
    for (MemberShape member : ShapeSupport.sortedMembers(target)) {
      if (!first) sb.append(", ");
      String smithyValue =
          member
              .getTrait(software.amazon.smithy.model.traits.EnumValueTrait.class)
              .flatMap(t -> t.getStringValue())
              .orElse(member.getMemberName());
      sb.append(CSharpNaming.formatString(smithyValue))
          .append(" => ")
          .append(grpcType)
          .append(".")
          .append(protoEnumMemberName(member.getMemberName()));
      first = false;
    }
    sb.append(", _ => throw new System.ArgumentOutOfRangeException(nameof(")
        .append(expr)
        .append(")) }");
    return sb.toString();
  }

  private static String grpcStringEnumToSmithy(SymbolProvider sp, EnumShape target, String expr) {
    String smithyType = CSharpSymbolProvider.qualified(sp.toSymbol(target));
    StringBuilder sb = new StringBuilder(expr).append(" switch { ");
    boolean first = true;
    for (MemberShape member : ShapeSupport.sortedMembers(target)) {
      if (!first) sb.append(", ");
      sb.append("_ when ")
          .append(expr)
          .append(".ToString() == ")
          .append(CSharpNaming.formatString(protoEnumMemberName(member.getMemberName())))
          .append(" => ")
          .append(smithyType)
          .append(".")
          .append(CSharpNaming.propertyName(member.getMemberName()));
      first = false;
    }
    sb.append(", _ => new ").append(smithyType).append("(").append(expr).append(".ToString()) }");
    return sb.toString();
  }

  private static boolean isProtoOptionalScalar(Model model, MemberShape m) {
    if (!ShapeSupport.isNullable(m)) return false;
    Shape target = model.expectShape(m.getTarget());
    return switch (target.getType()) {
      case STRING, BLOB, BOOLEAN, BYTE, SHORT, INTEGER, LONG, FLOAT, DOUBLE, ENUM, INT_ENUM -> true;
      default -> false;
    };
  }

  private static String nullGuard(MemberShape member, String expr, String converted) {
    return ShapeSupport.isNullable(member) ? expr + " is null ? null : " + converted : converted;
  }

  private static String smithyUnionToGrpcOneof(
      SymbolProvider sp, Model model, Shape target, String expr, String grpcNs) {
    if (!target.hasTrait(PROTO_INLINED_ONE_OF)) {
      throw new IllegalArgumentException(
          "Non-inlined gRPC union conversion is not implemented for: " + target.getId());
    }
    StringBuilder sb = new StringBuilder(expr).append(" switch { ");
    boolean first = true;
    for (MemberShape member : ShapeSupport.sortedMembers(target)) {
      if (!first) sb.append(", ");
      Shape memberTarget = model.expectShape(member.getTarget());
      String smithyUnionType = CSharpSymbolProvider.qualified(sp.toSymbol(target));
      String caseType = smithyUnionType + "." + CSharpNaming.propertyName(member.getMemberName());
      sb.append(caseType)
          .append(" value => ")
          .append(smithyToGrpcValueExpr(sp, model, memberTarget, member, "value.Value", grpcNs));
      first = false;
    }
    sb.append(", _ => throw new System.ArgumentOutOfRangeException(nameof(")
        .append(expr)
        .append(")) }");
    return sb.toString();
  }

  private static String smithyUnionToGrpcMessage(
      SymbolProvider sp, Model model, Shape target, String expr, String grpcNs) {
    String grpcType = "global::" + grpcNs + "." + CSharpNaming.typeName(target.getId().getName());
    StringBuilder sb =
        new StringBuilder("((System.Func<")
            .append(grpcType)
            .append(">)(() => { var message = new ")
            .append(grpcType)
            .append("(); switch (")
            .append(expr)
            .append(") { ");
    String smithyUnionType = CSharpSymbolProvider.qualified(sp.toSymbol(target));
    for (MemberShape member : ShapeSupport.sortedMembers(target)) {
      Shape memberTarget = model.expectShape(member.getTarget());
      String prop = CSharpNaming.propertyName(member.getMemberName());
      sb.append("case ")
          .append(smithyUnionType)
          .append(".")
          .append(prop)
          .append(" value: message.")
          .append(prop)
          .append(" = ")
          .append(smithyToGrpcValueExpr(sp, model, memberTarget, member, "value.Value", grpcNs))
          .append("; break; ");
    }
    sb.append("default: throw new System.ArgumentOutOfRangeException(nameof(")
        .append(expr)
        .append(")); } return message; }))()");
    return sb.toString();
  }

  private static String grpcOneofToSmithyUnion(
      SymbolProvider sp,
      Model model,
      StructureShape parent,
      MemberShape unionMember,
      Shape target,
      String src,
      String grpcNs) {
    String grpcParentType = CSharpNaming.typeName(parent.getId().getName());
    String smithyUnionType = CSharpSymbolProvider.qualified(sp.toSymbol(target));
    String oneofCase = CSharpNaming.propertyName(unionMember.getMemberName()) + "Case";
    String oneofCaseType = CSharpNaming.propertyName(unionMember.getMemberName()) + "OneofCase";
    StringBuilder sb = new StringBuilder(src).append(".").append(oneofCase).append(" switch { ");
    boolean first = true;
    for (MemberShape member : ShapeSupport.sortedMembers(target)) {
      if (!first) sb.append(", ");
      Shape memberTarget = model.expectShape(member.getTarget());
      String prop = CSharpNaming.propertyName(member.getMemberName());
      sb.append("global::")
          .append(grpcNs)
          .append(".")
          .append(grpcParentType)
          .append(".")
          .append(oneofCaseType)
          .append(".")
          .append(prop)
          .append(" => ")
          .append(smithyUnionType)
          .append(".From")
          .append(prop)
          .append("(")
          .append(
              grpcToSmithyNonNullableValueExpr(
                  sp, model, memberTarget, member, src + "." + prop, grpcNs))
          .append(")");
      first = false;
    }
    sb.append(", _ => null }");
    return sb.toString();
  }

  private static String grpcUnionMessageToSmithy(
      SymbolProvider sp, Model model, Shape target, String src, String grpcNs) {
    String grpcType = "global::" + grpcNs + "." + CSharpNaming.typeName(target.getId().getName());
    String smithyUnionType = CSharpSymbolProvider.qualified(sp.toSymbol(target));
    StringBuilder sb = new StringBuilder(src).append(".ValueCase switch { ");
    boolean first = true;
    for (MemberShape member : ShapeSupport.sortedMembers(target)) {
      if (!first) sb.append(", ");
      Shape memberTarget = model.expectShape(member.getTarget());
      String prop = CSharpNaming.propertyName(member.getMemberName());
      sb.append(grpcType)
          .append(".ValueOneofCase.")
          .append(prop)
          .append(" => ")
          .append(smithyUnionType)
          .append(".From")
          .append(prop)
          .append("(")
          .append(
              grpcToSmithyNonNullableValueExpr(
                  sp, model, memberTarget, member, src + "." + prop, grpcNs))
          .append(")");
      first = false;
    }
    sb.append(", _ => throw new System.ArgumentOutOfRangeException(nameof(")
        .append(src)
        .append(")) }");
    return sb.toString();
  }

  private static void appendSmithyUnionToGrpcOneofAssignments(
      StringBuilder sb, SymbolProvider sp, Model model, Shape target, String expr, String grpcNs) {
    String smithyUnionType = CSharpSymbolProvider.qualified(sp.toSymbol(target));
    sb.append("switch (").append(expr).append(") { ");
    for (MemberShape member : ShapeSupport.sortedMembers(target)) {
      Shape memberTarget = model.expectShape(member.getTarget());
      String prop = CSharpNaming.propertyName(member.getMemberName());
      sb.append("case ")
          .append(smithyUnionType)
          .append(".")
          .append(prop)
          .append(" value: message.")
          .append(prop)
          .append(" = ")
          .append(smithyToGrpcValueExpr(sp, model, memberTarget, member, "value.Value", grpcNs))
          .append("; break; ");
    }
    sb.append("default: throw new System.ArgumentOutOfRangeException(nameof(")
        .append(expr)
        .append(")); } ");
  }

  private static String protoEnumMemberName(String name) {
    StringBuilder sb = new StringBuilder();
    for (String part : name.split("_")) {
      if (part.isEmpty()) continue;
      sb.append(Character.toUpperCase(part.charAt(0)));
      if (part.length() > 1) {
        sb.append(part.substring(1).toLowerCase(java.util.Locale.ROOT));
      }
    }
    return sb.toString();
  }
}
