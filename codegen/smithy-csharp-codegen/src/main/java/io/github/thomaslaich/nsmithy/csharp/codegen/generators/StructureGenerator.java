/*
 * Renders a Smithy structure as a plain C# positional record. Constructor
 * parameters are required-first then optional so C# optional parameter rules
 * are preserved.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class StructureGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final StructureShape shape;

  public StructureGenerator(GenerationContext c, CSharpWriter w, StructureShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    List<MemberShape> members = List.copyOf(shape.members());

    writer.write("public sealed record class $L$L;", typeName, primaryConstructorParameters(sp));
    writer.write("");
    SchemaGenerator.writeFunctionalStructureSchema(writer, context, shape, members);
  }

  private String primaryConstructorParameters(SymbolProvider sp) {
    List<MemberShape> ctorMembers = ShapeSupport.constructorMembers(shape);
    if (ctorMembers.isEmpty()) {
      return "";
    }

    StringBuilder sig = new StringBuilder("(");
    for (int i = 0; i < ctorMembers.size(); i++) {
      MemberShape member = ctorMembers.get(i);
      sig.append(ShapeSupport.parameterTypeExpr(sp, member))
          .append(' ')
          .append(CSharpNaming.propertyName(member.getMemberName()));
      if (ShapeSupport.isOptionalParameter(member)) {
        sig.append(" = null");
      }
      if (i < ctorMembers.size() - 1) sig.append(", ");
    }
    sig.append(")");
    return sig.toString();
  }
}
