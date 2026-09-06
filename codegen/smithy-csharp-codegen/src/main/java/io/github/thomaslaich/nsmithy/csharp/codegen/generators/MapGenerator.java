/*
 * Renders a Smithy map as a C# wrapper record over IReadOnlyDictionary<TKey, TValue>.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class MapGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final MapShape shape;

  public MapGenerator(GenerationContext c, CSharpWriter w, MapShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    Symbol value = sp.toSymbol(context.model().expectShape(shape.getValue().getTarget()));
    // Always a string, even when the key targets an enum shape: a map key is a JSON object name,
    // which has no other form. What the key targets is not lost — the schema carries that shape, so
    // a server holds the key to whatever it says — but it is not what the key is typed as.
    String keyType = "string";
    String valueType = writer.typeName(value) + (ShapeSupport.isSparse(shape) ? "?" : "");

    writer.writeXmlDocs(shape);
    writer.write("public sealed partial record class $L", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "public $L($T<$L, $L> values)",
              typeName,
              RuntimeTypes.I_READ_ONLY_DICTIONARY,
              keyType,
              valueType);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("$T.ThrowIfNull(values);", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
                writer.write(
                    "Values = new $T<$L, $L>(new $T<$L, $L>(values));",
                    RuntimeTypes.READ_ONLY_DICTIONARY,
                    keyType,
                    valueType,
                    RuntimeTypes.DICTIONARY,
                    keyType,
                    valueType);
              });
          writer.write("");
          writer.write(
              "private $L($T<$L, $L> values)",
              typeName,
              RuntimeTypes.DICTIONARY,
              keyType,
              valueType);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "Values = new $T<$L, $L>(values);",
                      RuntimeTypes.READ_ONLY_DICTIONARY,
                      keyType,
                      valueType));
          writer.write("");
          writer.write(
              "internal static $L FromOwnedDictionary($T<$L, $L> values) => new(values);",
              typeName,
              RuntimeTypes.DICTIONARY,
              keyType,
              valueType);
          writer.write("");
          writer.writeXmlDocs(shape.getValue());
          writer.write(
              "public $T<$L, $L> Values { get; }",
              RuntimeTypes.I_READ_ONLY_DICTIONARY,
              keyType,
              valueType);
        });
    writer.write("");
    SchemaGenerator.writeMapSchema(writer, context, shape);
  }
}
