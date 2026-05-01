/*
 * Renders a Smithy map as a C# wrapper record over IReadOnlyDictionary<TKey, TValue>.
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
    Symbol key = sp.toSymbol(context.model().expectShape(shape.getKey().getTarget()));
    Symbol value = sp.toSymbol(context.model().expectShape(shape.getValue().getTarget()));
    String keyType = CSharpSymbolProvider.qualified(key);
    String valueType =
        CSharpSymbolProvider.qualified(value) + (ShapeSupport.isSparse(shape) ? "?" : "");

    AttributeEmitter.writeShapeAttributes(writer, shape);
    writer.openBlock(
        "public sealed partial record class $L {",
        "}",
        typeName,
        () -> {
          writer.write("");
          writer.openBlock(
              "public $L(System.Collections.Generic.IReadOnlyDictionary<$L, $L> values) {",
              "}",
              typeName,
              keyType,
              valueType,
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
          AttributeEmitter.writeMemberAttributes(
              writer, shape.getValue(), ShapeSupport.isSparse(shape));
          writer.write(
              "public System.Collections.Generic.IReadOnlyDictionary<$L, $L> Values { get; }",
              keyType,
              valueType);
        });
  }
}
