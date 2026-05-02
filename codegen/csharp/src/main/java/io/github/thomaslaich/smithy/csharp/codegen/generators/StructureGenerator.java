/*
 * Renders a Smithy structure as a C# `public sealed partial record class`.
 * Constructor parameters are required-first then optional, members are
 * exposed as get-only properties, all decorated with [SmithyMember].
 */
package io.github.thomaslaich.smithy.csharp.codegen.generators;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.smithy.csharp.codegen.support.AttributeEmitter;
import io.github.thomaslaich.smithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
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
    Model model = context.model();
    String typeName = CSharpNaming.typeName(shape.getId().getName());

    AttributeEmitter.writeShapeAttributes(writer, shape);
    writer.openBlock(
        "public sealed partial record class $L {",
        "}",
        typeName,
        () -> {
          writer.write("");
          writeConstructor(typeName);
          writeProperties(sp, model);
        });
  }

  private void writeConstructor(String typeName) {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    List<MemberShape> ctorMembers = ShapeSupport.constructorMembers(shape);
    if (ctorMembers.isEmpty()) {
      writer.write("public $L() { }", typeName);
      writer.write("");
      return;
    }

    StringBuilder sig = new StringBuilder("public ").append(typeName).append("(");
    for (int i = 0; i < ctorMembers.size(); i++) {
      MemberShape m = ctorMembers.get(i);
      sig.append(ShapeSupport.parameterTypeExpr(sp, m))
          .append(' ')
          .append(CSharpNaming.parameterName(m.getMemberName()));
      if (ShapeSupport.isOptionalParameter(m)) {
        sig.append(" = null");
      }
      if (i < ctorMembers.size() - 1) sig.append(", ");
    }
    sig.append(")");

    writer.openBlock(
        sig.toString() + " {",
        "}",
        () -> {
          for (MemberShape m : ctorMembers) {
            String prop = CSharpNaming.propertyName(m.getMemberName());
            String param = CSharpNaming.parameterName(m.getMemberName());
            String defaultExpr = ShapeSupport.defaultValueExpression(model, sp, m);
            if (defaultExpr != null) {
              writer.write("$L = $L ?? $L;", prop, param, defaultExpr);
            } else if (!ShapeSupport.isNullable(m) && ShapeSupport.isReferenceType(model, m)) {
              writer.write(
                  "$L = $L ?? throw new System.ArgumentNullException(nameof($L));",
                  prop,
                  param,
                  param);
            } else {
              writer.write("$L = $L;", prop, param);
            }
          }
        });
    writer.write("");
  }

  private void writeProperties(SymbolProvider sp, Model model) {
    for (MemberShape m : ShapeSupport.sortedMembers(shape)) {
      String prop = CSharpNaming.propertyName(m.getMemberName());
      boolean nullable = ShapeSupport.isNullable(m);
      String type = ShapeSupport.memberTypeExpr(sp, m, nullable);
      AttributeEmitter.writeMemberAttributes(writer, m, ShapeSupport.isSparseTarget(model, m));
      writer.write("public $L $L { get; }", type, prop);
    }
  }
}
