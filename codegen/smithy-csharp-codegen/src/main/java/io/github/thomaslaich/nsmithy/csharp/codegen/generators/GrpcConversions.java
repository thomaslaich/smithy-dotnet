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
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import java.util.Optional;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class GrpcConversions {

  private static final ShapeId PROTO_NUM_TYPE = ShapeId.from("alloy.proto#protoNumType");

  private GrpcConversions() {}

  /**
   * Build a gRPC message instance from a Smithy structure value. {@code src} is the C# expression
   * yielding the Smithy struct (e.g. "input" or "output.Foo").
   */
  public static String smithyToGrpc(
      SymbolProvider sp, Model model, Shape shape, String src, String grpcNs) {
    if (!(shape instanceof StructureShape s)) return src;
    String grpcType = "global::" + grpcNs + "." + CSharpNaming.typeName(s.getId().getName());
    StringBuilder sb = new StringBuilder("new ").append(grpcType).append(" {");
    boolean first = true;
    for (MemberShape m : ShapeSupport.sortedMembers(s)) {
      if (!first) sb.append(",");
      sb.append(" ");
      sb.append(CSharpNaming.propertyName(m.getMemberName()));
      sb.append(" = ");
      String propAccess = src + "." + CSharpNaming.propertyName(m.getMemberName());
      sb.append(smithyToGrpcMemberExpr(model, m, propAccess));
      first = false;
    }
    sb.append(" }");
    return sb.toString();
  }

  /**
   * Build a Smithy structure value from a gRPC message. {@code src} is the C# expression yielding
   * the gRPC message (e.g. "response").
   */
  public static String grpcToSmithy(SymbolProvider sp, Model model, Shape shape, String src) {
    if (!(shape instanceof StructureShape s)) return src;
    String smithyType =
        io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider.qualified(sp.toSymbol(s));
    StringBuilder sb = new StringBuilder("new ").append(smithyType).append("(");
    var members = ShapeSupport.constructorMembers(s);
    for (int i = 0; i < members.size(); i++) {
      if (i > 0) sb.append(", ");
      MemberShape m = members.get(i);
      String propAccess = src + "." + CSharpNaming.propertyName(m.getMemberName());
      sb.append(grpcToSmithyMemberExpr(model, m, propAccess));
    }
    sb.append(")");
    return sb.toString();
  }

  /**
   * Returns the expression for assigning a Smithy member value to a proto field. Adds an explicit
   * cast when @protoNumType("UNSIGNED") or @protoNumType("FIXED") maps an integer to uint/ulong.
   * Nullable Smithy members are null-coalesced to 0 before casting (proto fields have no null).
   */
  private static String smithyToGrpcMemberExpr(Model model, MemberShape m, String expr) {
    Optional<String> numType =
        m.findTrait(PROTO_NUM_TYPE).map(t -> t.toNode().expectStringNode().getValue());
    if (numType.isPresent()) {
      String nt = numType.get();
      if ("UNSIGNED".equals(nt) || "FIXED".equals(nt)) {
        Shape target = model.expectShape(m.getTarget());
        boolean isLong = target.getType() == ShapeType.LONG;
        boolean nullable = ShapeSupport.isNullable(m);
        String innerExpr = nullable ? "(" + expr + " ?? 0)" : expr;
        return "(" + (isLong ? "ulong" : "uint") + ")" + innerExpr;
      }
    }
    return expr;
  }

  /**
   * Returns the expression for constructing a Smithy member value from a proto field. Adds an
   * explicit cast when @protoNumType("UNSIGNED") or @protoNumType("FIXED") maps an integer to
   * uint/ulong in proto while the Smithy type is int/long (possibly nullable).
   */
  private static String grpcToSmithyMemberExpr(Model model, MemberShape m, String expr) {
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
}
