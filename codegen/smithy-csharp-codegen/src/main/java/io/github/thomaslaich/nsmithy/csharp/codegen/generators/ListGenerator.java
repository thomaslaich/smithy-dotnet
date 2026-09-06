/*
 * Renders a Smithy list/set as a C# wrapper record over IReadOnlyList<T>.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
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
    String memberType = writer.typeName(member) + (ShapeSupport.isSparse(shape) ? "?" : "");

    writer.writeXmlDocs(shape);
    writer.write("public sealed partial record class $L", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public $L($T<$L> values)", typeName, RuntimeTypes.I_ENUMERABLE, memberType);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("$T.ThrowIfNull(values);", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
                writer.write(
                    "Values = $T.AsReadOnly($T.ToArray(values));",
                    RuntimeTypes.ARRAY,
                    RuntimeTypes.ENUMERABLE);
              });
          writer.write("");
          writer.write("private $L($T<$L> values)", typeName, RuntimeTypes.LIST, memberType);
          writer.openBlock("{", "}", () -> writer.write("Values = values.AsReadOnly();"));
          writer.write("");
          writer.write(
              "internal static $L FromOwnedList($T<$L> values) => new(values);",
              typeName,
              RuntimeTypes.LIST,
              memberType);
          writer.write("");
          writer.writeXmlDocs(shape.getMember());
          writer.write("public $T<$L> Values { get; }", RuntimeTypes.I_READ_ONLY_LIST, memberType);
        });
    writer.write("");
    SchemaGenerator.writeListSchema(writer, context, shape);
  }
}
