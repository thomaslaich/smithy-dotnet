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
package io.github.thomaslaich.smithy.csharp.codegen.generators;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.support.ShapeSupport;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class GrpcConversions {

  private GrpcConversions() {}

  /**
   * Build a gRPC message instance from a Smithy structure value. {@code src} is the C# expression
   * yielding the Smithy struct (e.g. "input" or "output.Foo").
   */
  public static String smithyToGrpc(SymbolProvider sp, Shape shape, String src, String grpcNs) {
    if (!(shape instanceof StructureShape s)) return src;
    String grpcType = "global::" + grpcNs + "." + CSharpNaming.typeName(s.getId().getName());
    StringBuilder sb = new StringBuilder("new ").append(grpcType).append(" {");
    boolean first = true;
    for (MemberShape m : ShapeSupport.sortedMembers(s)) {
      if (!first) sb.append(",");
      sb.append(" ");
      sb.append(CSharpNaming.propertyName(m.getMemberName()));
      sb.append(" = ");
      sb.append(src).append(".").append(CSharpNaming.propertyName(m.getMemberName()));
      first = false;
    }
    sb.append(" }");
    return sb.toString();
  }

  /**
   * Build a Smithy structure value from a gRPC message. {@code src} is the C# expression yielding
   * the gRPC message (e.g. "response").
   */
  public static String grpcToSmithy(SymbolProvider sp, Shape shape, String src) {
    if (!(shape instanceof StructureShape s)) return src;
    String smithyType =
        io.github.thomaslaich.smithy.csharp.codegen.CSharpSymbolProvider.qualified(sp.toSymbol(s));
    StringBuilder sb = new StringBuilder("new ").append(smithyType).append("(");
    var members = ShapeSupport.constructorMembers(s);
    for (int i = 0; i < members.size(); i++) {
      if (i > 0) sb.append(", ");
      sb.append(src).append(".").append(CSharpNaming.propertyName(members.get(i).getMemberName()));
    }
    sb.append(")");
    return sb.toString();
  }
}
