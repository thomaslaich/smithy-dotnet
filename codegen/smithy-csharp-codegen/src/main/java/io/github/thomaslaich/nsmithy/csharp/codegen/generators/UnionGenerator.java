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
                              "Value = value ?? throw new"
                                  + " System.ArgumentNullException(nameof(value));");
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

          writer.addImport(RuntimeTypes.NSMITHY_CORE);
          writer.write("public sealed partial record class Unknown : $L", typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("public Unknown(string tag, Document value)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write(
                          "Tag = tag ?? throw new System.ArgumentNullException(nameof(tag));");
                      writer.write("Value = value;");
                    });
                writer.write("");
                writer.write("public string Tag { get; }");
                writer.write("public Document Value { get; }");
              });
          writer.write("");
          writer.write("public static $L FromUnknown(string tag, Document value)", typeName);
          writer.openBlock("{", "}", () -> writer.write("return new Unknown(tag, value);"));
          writer.write("");

          // Match method
          StringBuilder header = new StringBuilder("public T Match<T>(");
          for (MemberShape m : members) {
            String pn = CSharpNaming.parameterName(m.getMemberName());
            String vt = ShapeSupport.memberTypeExpr(writer, model, sp, m, false);
            header.append("System.Func<").append(vt).append(", T> ").append(pn).append(", ");
          }
          header.append("System.Func<string, Document, T> unknown)");
          writer.write(header.toString());
          writer.openBlock(
              "{",
              "}",
              () -> {
                for (MemberShape m : members) {
                  String pn = CSharpNaming.parameterName(m.getMemberName());
                  writer.write("System.ArgumentNullException.ThrowIfNull($L);", pn);
                }
                writer.write("System.ArgumentNullException.ThrowIfNull(unknown);");
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
                    "_ => throw new System.InvalidOperationException(\"Unknown union variant.\"),");
                writer.dedent();
                writer.write("};");
              });
        });
    writer.write("");
    SchemaGenerator.writeUnionSchema(writer, context, shape, members);
  }
}
