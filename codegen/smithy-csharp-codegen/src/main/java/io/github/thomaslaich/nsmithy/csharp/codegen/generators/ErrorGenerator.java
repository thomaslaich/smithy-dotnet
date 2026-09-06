/*
 * Renders a Smithy @error structure as a C# Exception subclass.
 * The first constructor parameter is the message (forwarded to base(message)),
 * additional members follow the same nullability conventions as a structure.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.model.traits.RetryableTrait;
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
    writer.reserveMemberNames(shape);
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    Optional<MemberShape> messageMember = ShapeSupport.errorMessageMember(model, shape);
    List<MemberShape> members = ShapeSupport.sortedMembers(shape);

    Optional<RetryableTrait> retryable = shape.getTrait(RetryableTrait.class);
    writer.writeXmlDocs(shape);
    if (retryable.isPresent()) {
      writer.write(
          "public sealed partial class $L : $T, $T",
          typeName,
          RuntimeTypes.EXCEPTION,
          RuntimeTypes.I_SMITHY_RETRYABLE_ERROR);
    } else {
      writer.write("public sealed partial class $L : $T", typeName, RuntimeTypes.EXCEPTION);
    }
    writer.openBlock(
        "{",
        "}",
        () -> {
          retryable.ifPresent(
              trait -> {
                writer.write(
                    "bool $T.IsThrottlingError => $L;",
                    RuntimeTypes.I_SMITHY_RETRYABLE_ERROR,
                    trait.getThrottling() ? "true" : "false");
                writer.write("");
              });
          writeConstructor(typeName, messageMember.orElse(null));
          messageMember.ifPresent(
              mm -> {
                writer.writeXmlDocs(mm);
                writer.write("public override string Message => base.Message!;");
                writer.write("");
              });
          writeProperties(sp, model, messageMember.orElse(null));
        });
    writer.write("");
    SchemaGenerator.writeStructureSchema(writer, context, shape, members);
  }

  private void writeConstructor(String typeName, MemberShape messageMember) {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    List<MemberShape> ctor = ShapeSupport.constructorMembers(shape, messageMember);
    boolean hasRequired = ctor.stream().anyMatch(m -> !ShapeSupport.isOptionalParameter(m));

    Map<String, String> parameterDocs = new LinkedHashMap<>();
    if (messageMember != null) {
      messageMember
          .getTrait(DocumentationTrait.class)
          .ifPresent(trait -> parameterDocs.put("message", trait.getValue()));
    }
    for (MemberShape m : ctor) {
      m.getTrait(DocumentationTrait.class)
          .ifPresent(
              trait ->
                  parameterDocs.put(
                      CSharpNaming.parameterName(m.getMemberName()), trait.getValue()));
    }
    writer.writeXmlDocs(shape, parameterDocs);

    StringBuilder sig = new StringBuilder("public ").append(typeName).append("(");
    sig.append("string? message");
    if (!hasRequired) sig.append(" = null");
    for (MemberShape m : ctor) {
      sig.append(", ")
          .append(ShapeSupport.parameterTypeExpr(writer, model, sp, m))
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
                    "$L = $L ?? throw new $T(nameof($L));",
                    prop,
                    param,
                    RuntimeTypes.ARGUMENT_NULL_EXCEPTION,
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
      String type = ShapeSupport.memberTypeExpr(writer, model, sp, m, nullable);
      writer.writeXmlDocs(m);
      writer.write("public $L $L { get; }", type, prop);
    }
  }
}
