/*
 * Renders a Smithy @error structure as a C# Exception subclass.
 * The first constructor parameter is the message (forwarded to base(message)),
 * additional members follow the same nullability conventions as a structure.
 */
package io.github.thomaslaich.smithy.csharp.codegen.generators;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.smithy.csharp.codegen.support.AttributeEmitter;
import io.github.thomaslaich.smithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ErrorGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final StructureShape shape;

  public ErrorGenerator(GenerationContext c, CSharpWriter w, StructureShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    Optional<MemberShape> messageMember = ShapeSupport.errorMessageMember(shape);

    AttributeEmitter.writeShapeAttributes(writer, shape);
    writer.openBlock(
        "public sealed partial class $L : System.Exception {",
        "}",
        typeName,
        () -> {
          writer.write("");
          writeConstructor(typeName, messageMember.orElse(null));
          messageMember.ifPresent(
              mm -> {
                AttributeEmitter.writeMemberAttributes(writer, mm, false);
                writer.write("public override string Message => base.Message!;");
                writer.write("");
              });
          writeProperties(sp, model, messageMember.orElse(null));
        });
  }

  private void writeConstructor(String typeName, MemberShape messageMember) {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    List<MemberShape> ctor = ShapeSupport.constructorMembers(shape, messageMember);
    boolean hasRequired = ctor.stream().anyMatch(m -> !ShapeSupport.isOptionalParameter(m));

    StringBuilder sig = new StringBuilder("public ").append(typeName).append("(");
    sig.append("string? message");
    if (!hasRequired) sig.append(" = null");
    for (MemberShape m : ctor) {
      sig.append(", ")
          .append(ShapeSupport.parameterTypeExpr(sp, m))
          .append(' ')
          .append(CSharpNaming.parameterName(m.getMemberName()));
      if (ShapeSupport.isOptionalParameter(m)) sig.append(" = null");
    }
    sig.append(")");
    writer.write(sig.toString());
    writer.write("    : base(message)");
    if (ctor.isEmpty()) {
      writer.write("{ }");
    } else {
      writer.openBlock(
          "{",
          "}",
          () -> {
            for (MemberShape m : ctor) {
              String prop = CSharpNaming.propertyName(m.getMemberName());
              String param = CSharpNaming.parameterName(m.getMemberName());
              if (!ShapeSupport.isNullable(m) && ShapeSupport.isReferenceType(model, m)) {
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
    }
    writer.write("");
  }

  private void writeProperties(SymbolProvider sp, Model model, MemberShape excluded) {
    for (MemberShape m : ShapeSupport.sortedMembers(shape, excluded)) {
      String prop = CSharpNaming.propertyName(m.getMemberName());
      boolean nullable = ShapeSupport.isNullable(m);
      String type = ShapeSupport.memberTypeExpr(sp, m, nullable);
      AttributeEmitter.writeMemberAttributes(writer, m, ShapeSupport.isSparseTarget(model, m));
      writer.write("public $L $L { get; }", type, prop);
    }
  }
}
