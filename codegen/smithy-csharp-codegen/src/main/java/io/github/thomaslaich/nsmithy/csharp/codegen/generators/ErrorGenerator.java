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
    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      writer.putContext("exception", RuntimeTypes.EXCEPTION);
      writer.putContext(
          "retryableInterface",
          retryable.isPresent()
              ? writer.format(", $T", RuntimeTypes.I_SMITHY_RETRYABLE_ERROR)
              : "");
      writer.putContext(
          "members",
          writer.consumer(
              w -> {
                retryable.ifPresent(
                    trait -> {
                      w.write(
                          "bool $T.IsThrottlingError => $L;",
                          RuntimeTypes.I_SMITHY_RETRYABLE_ERROR,
                          trait.getThrottling() ? "true" : "false");
                      w.write("");
                    });
                writeConstructor(typeName, messageMember.orElse(null));
                messageMember.ifPresent(
                    member -> {
                      w.writeXmlDocs(member);
                      w.write("public override string Message => base.Message!;");
                      w.write("");
                    });
                writeProperties(sp, model, messageMember.orElse(null));
              }));
      writer.writeXmlDocs(shape);
      writer.write(
          """
          public sealed partial class ${typeName:L} : ${exception:T}${retryableInterface:L}
          {
              ${members:C|}
          }
          """);
    } finally {
      writer.popState();
    }
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

    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      if (ctor.isEmpty()) {
        writer.write(
            """
            public ${typeName:L}(string? message = null)
                : base(message)
            { }
            """);
      } else {
        writer.putContext(
            "parameters",
            writer.consumer(
                w -> {
                  w.write("string? message$L,", hasRequired ? "" : " = null");
                  for (int i = 0; i < ctor.size(); i++) {
                    MemberShape member = ctor.get(i);
                    w.write(
                        "$L $L$L$L",
                        ShapeSupport.parameterTypeExpr(w, model, sp, member),
                        CSharpNaming.parameterName(member.getMemberName()),
                        ShapeSupport.isOptionalParameter(member) ? " = null" : "",
                        i + 1 == ctor.size() ? "" : ",");
                  }
                }));
        writer.putContext("assignments", writer.consumer(w -> writeAssignments(ctor)));
        writer.write(
            """
            public ${typeName:L}(
                ${parameters:C|}
            )
                : base(message)
            {
                ${assignments:C|}
            }
            """);
      }
    } finally {
      writer.popState();
    }
    writer.write("");
  }

  private void writeAssignments(List<MemberShape> members) {
    for (MemberShape member : members) {
      String property = CSharpNaming.propertyName(member.getMemberName());
      String parameter = CSharpNaming.parameterName(member.getMemberName());
      if (!ShapeSupport.isNullable(member)
          && ShapeSupport.isReferenceType(context.model(), member)) {
        writer.write(
            "$L = $L ?? throw new $T(nameof($L));",
            property,
            parameter,
            RuntimeTypes.ARGUMENT_NULL_EXCEPTION,
            parameter);
      } else {
        writer.write("$L = $L;", property, parameter);
      }
    }
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
