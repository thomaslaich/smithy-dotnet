/*
 * Renders a Smithy list/set as a C# wrapper record over IReadOnlyList<T>.
 */
package io.github.thomaslaich.smithy.csharp.codegen.generators;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.smithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.smithy.csharp.codegen.support.AttributeEmitter;
import io.github.thomaslaich.smithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ListGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ListShape shape;

  public ListGenerator(GenerationContext c, CSharpWriter w, ListShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    Symbol member = sp.toSymbol(context.model().expectShape(shape.getMember().getTarget()));
    String memberType =
        CSharpSymbolProvider.qualified(member) + (ShapeSupport.isSparse(shape) ? "?" : "");

    AttributeEmitter.writeShapeAttributes(writer, shape);
    writer.openBlock(
        "public sealed partial record class $L {",
        "}",
        typeName,
        () -> {
          writer.write("");
          writer.openBlock(
              "public $L(System.Collections.Generic.IEnumerable<$L> values) {",
              "}",
              typeName,
              memberType,
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(values);");
                writer.write(
                    "Values = System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(values));");
              });
          writer.write("");
          AttributeEmitter.writeMemberAttributes(
              writer, shape.getMember(), ShapeSupport.isSparse(shape));
          writer.write(
              "public System.Collections.Generic.IReadOnlyList<$L> Values { get; }", memberType);
        });
  }
}
