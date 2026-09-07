/*
 * Renders a Smithy union as a C# abstract record class with a sealed nested
 * record per variant plus a `Match` method for pattern-matching consumers.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class UnionGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final UnionShape shape;

  public UnionGenerator(GenerationContext c, CSharpWriter w, UnionShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    writer.reserveMemberNames(shape);
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    List<MemberShape> members = ShapeSupport.sortedMembers(shape);

    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      writer.putContext("document", RuntimeTypes.DOCUMENT);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext(
          "variants",
          writer.consumer(
              w -> {
                for (MemberShape member : members) {
                  writeVariant(typeName, member);
                  w.write("");
                }
              }));
      writer.putContext("match", writer.consumer(w -> writeMatch(members)));
      writer.write(
          """
          public abstract partial record class ${typeName:L}
          {
              private protected ${typeName:L}() { }

              ${variants:C|}
              public sealed partial record class Unknown : ${typeName:L}
              {
                  public Unknown(string tag, ${document:T} value)
                  {
                      Tag = tag ?? throw new ${argumentNullException:T}(nameof(tag));
                      Value = value;
                  }

                  public string Tag { get; }
                  public ${document:T} Value { get; }
              }

              public static ${typeName:L} FromUnknown(string tag, ${document:T} value)
              {
                  return new Unknown(tag, value);
              }

              ${match:C|}
          }
          """);
    } finally {
      writer.popState();
    }
    writer.write("");
    SchemaGenerator.writeUnionSchema(writer, context, shape, members);
  }

  private void writeVariant(String typeName, MemberShape member) {
    Model model = context.model();
    String valueType =
        ShapeSupport.memberTypeExpr(writer, model, context.symbolProvider(), member, false);
    String valueExpression =
        ShapeSupport.isReferenceType(model, member)
            ? writer.format(
                "value ?? throw new $T(nameof(value))", RuntimeTypes.ARGUMENT_NULL_EXCEPTION)
            : "value";
    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      writer.putContext("variantName", CSharpNaming.typeName(member.getMemberName()));
      writer.putContext("valueType", valueType);
      writer.putContext("valueExpression", valueExpression);
      writer.write(
          """
          public sealed partial record class ${variantName:L} : ${typeName:L}
          {
              public ${variantName:L}(${valueType:L} value)
              {
                  Value = ${valueExpression:L};
              }

              public ${valueType:L} Value { get; }
          }

          public static ${typeName:L} From${variantName:L}(${valueType:L} value)
          {
              return new ${variantName:L}(value);
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeMatch(List<MemberShape> members) {
    writer.pushState();
    try {
      writer.putContext("func", RuntimeTypes.FUNC);
      writer.putContext("document", RuntimeTypes.DOCUMENT);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext("invalidOperationException", RuntimeTypes.INVALID_OPERATION_EXCEPTION);
      writer.putContext(
          "parameters",
          writer.consumer(
              w -> {
                for (MemberShape member : members) {
                  w.write(
                      "$T<$L, T> $L,",
                      RuntimeTypes.FUNC,
                      ShapeSupport.memberTypeExpr(
                          w, context.model(), context.symbolProvider(), member, false),
                      CSharpNaming.parameterName(member.getMemberName()));
                }
              }));
      writer.putContext(
          "nullChecks",
          writer.consumer(
              w -> {
                for (MemberShape member : members) {
                  w.write(
                      "$T.ThrowIfNull($L);",
                      RuntimeTypes.ARGUMENT_NULL_EXCEPTION,
                      CSharpNaming.parameterName(member.getMemberName()));
                }
              }));
      writer.putContext(
          "cases",
          writer.consumer(
              w -> {
                for (MemberShape member : members) {
                  w.write(
                      "$L value => $L(value.Value),",
                      CSharpNaming.typeName(member.getMemberName()),
                      CSharpNaming.parameterName(member.getMemberName()));
                }
              }));
      writer.write(
          """
          public T Match<T>(
              ${parameters:C|}
              ${func:T}<string, ${document:T}, T> unknown)
          {
              ${nullChecks:C|}
              ${argumentNullException:T}.ThrowIfNull(unknown);

              return this switch
              {
                  ${cases:C|}
                  Unknown value => unknown(value.Tag, value.Value),
                  _ => throw new ${invalidOperationException:T}("Unknown union variant."),
              };
          }
          """);
    } finally {
      writer.popState();
    }
  }
}
