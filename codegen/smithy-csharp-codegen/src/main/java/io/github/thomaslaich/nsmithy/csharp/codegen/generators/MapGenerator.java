/*
 * Renders a Smithy map as a C# wrapper record over IReadOnlyDictionary<TKey, TValue>.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
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
    // and the runtime's map schema is string-keyed throughout. The enum's value set still reaches
    // the schema as key traits, so the server holds the key to it.
    String keyType = "string";
    String valueType =
        CSharpSymbolProvider.qualified(value) + (ShapeSupport.isSparse(shape) ? "?" : "");

    writer.writeXmlDocs(shape);
    writer.write("public sealed partial record class $L", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "public $L(System.Collections.Generic.IReadOnlyDictionary<$L, $L> values)",
              typeName,
              keyType,
              valueType);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(values);");
                writer.write(
                    "Values = new System.Collections.ObjectModel.ReadOnlyDictionary<$L, $L>("
                        + "new System.Collections.Generic.Dictionary<$L, $L>(values));",
                    keyType,
                    valueType,
                    keyType,
                    valueType);
              });
          writer.write("");
          writer.writeXmlDocs(shape.getValue());
          writer.write(
              "public System.Collections.Generic.IReadOnlyDictionary<$L, $L> Values { get; }",
              keyType,
              valueType);
        });
    writer.write("");
    SchemaGenerator.writeMapSchema(writer, context, shape);
  }
}
