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
import software.amazon.smithy.codegen.core.SymbolProvider;
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
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    List<MemberShape> members = ShapeSupport.sortedMembers(shape);

    writer.write("public abstract partial record class $L", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("private protected $L() { }", typeName);
          writer.write("");
          for (MemberShape m : members) {
            String variantName = CSharpNaming.typeName(m.getMemberName());
            String valueType = ShapeSupport.memberTypeExpr(writer, model, sp, m, false);
            writer.write("public sealed partial record class $L : $L", variantName, typeName);
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writer.write("public $L($L value)", variantName, valueType);
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        if (ShapeSupport.isReferenceType(model, m)) {
                          writer.write(
                              "Value = value ?? throw new $T(nameof(value));",
                              RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
                        } else {
                          writer.write("Value = value;");
                        }
                      });
                  writer.write("");
                  writer.write("public $L Value { get; }", valueType);
                });
            writer.write("");
            writer.write("public static $L From$L($L value)", typeName, variantName, valueType);
            writer.openBlock("{", "}", () -> writer.write("return new $L(value);", variantName));
            writer.write("");
          }

          writer.write("public sealed partial record class Unknown : $L", typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("public Unknown(string tag, $T value)", RuntimeTypes.DOCUMENT);
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write(
                          "Tag = tag ?? throw new $T(nameof(tag));",
                          RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
                      writer.write("Value = value;");
                    });
                writer.write("");
                writer.write("public string Tag { get; }");
                writer.write("public $T Value { get; }", RuntimeTypes.DOCUMENT);
              });
          writer.write("");
          writer.write(
              "public static $L FromUnknown(string tag, $T value)",
              typeName,
              RuntimeTypes.DOCUMENT);
          writer.openBlock("{", "}", () -> writer.write("return new Unknown(tag, value);"));
          writer.write("");

          // Match method
          StringBuilder header = new StringBuilder("public T Match<T>(");
          for (MemberShape m : members) {
            String pn = CSharpNaming.parameterName(m.getMemberName());
            String vt = ShapeSupport.memberTypeExpr(writer, model, sp, m, false);
            header
                .append(writer.typeName(RuntimeTypes.FUNC) + "<")
                .append(vt)
                .append(", T> ")
                .append(pn)
                .append(", ");
          }
          header.append(
              writer.typeName(RuntimeTypes.FUNC)
                  + ("<string, " + writer.typeName(RuntimeTypes.DOCUMENT) + ", T> unknown)"));
          writer.write(header.toString());
          writer.openBlock(
              "{",
              "}",
              () -> {
                for (MemberShape m : members) {
                  String pn = CSharpNaming.parameterName(m.getMemberName());
                  writer.write("$T.ThrowIfNull($L);", RuntimeTypes.ARGUMENT_NULL_EXCEPTION, pn);
                }
                writer.write("$T.ThrowIfNull(unknown);", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
                writer.write("");
                writer.write("return this switch {");
                writer.indent();
                for (MemberShape m : members) {
                  String variantName = CSharpNaming.typeName(m.getMemberName());
                  String pn = CSharpNaming.parameterName(m.getMemberName());
                  writer.write("$L value => $L(value.Value),", variantName, pn);
                }
                writer.write("Unknown value => unknown(value.Tag, value.Value),");
                writer.write(
                    "_ => throw new $T(\"Unknown union variant.\"),",
                    RuntimeTypes.INVALID_OPERATION_EXCEPTION);
                writer.dedent();
                writer.write("};");
              });
        });
    writer.write("");
    SchemaGenerator.writeUnionSchema(writer, context, shape, members);
  }
}
