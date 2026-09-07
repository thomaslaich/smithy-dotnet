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
    String valueType = writer.typeName(value) + (ShapeSupport.isSparse(shape) ? "?" : "");

    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      writer.putContext("valueType", valueType);
      writer.putContext("readOnlyDictionaryInterface", RuntimeTypes.I_READ_ONLY_DICTIONARY);
      writer.putContext("readOnlyDictionary", RuntimeTypes.READ_ONLY_DICTIONARY);
      writer.putContext("dictionary", RuntimeTypes.DICTIONARY);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext(
          "valuesProperty",
          writer.consumer(
              w -> {
                w.writeXmlDocs(shape.getValue());
                w.write(
                    "public $T<string, $L> Values { get; }",
                    RuntimeTypes.I_READ_ONLY_DICTIONARY,
                    valueType);
              }));
      writer.writeXmlDocs(shape);
      writer.write(
          """
          public sealed partial record class ${typeName:L}
          {
              public ${typeName:L}(${readOnlyDictionaryInterface:T}<string, ${valueType:L}> values)
              {
                  ${argumentNullException:T}.ThrowIfNull(values);
                  Values = new ${readOnlyDictionary:T}<string, ${valueType:L}>(
                      new ${dictionary:T}<string, ${valueType:L}>(values));
              }

              private ${typeName:L}(${dictionary:T}<string, ${valueType:L}> values)
              {
                  Values = new ${readOnlyDictionary:T}<string, ${valueType:L}>(values);
              }

              internal static ${typeName:L} FromOwnedDictionary(
                  ${dictionary:T}<string, ${valueType:L}> values) => new(values);

              ${valuesProperty:C|}
          }
          """);
    } finally {
      writer.popState();
    }
    writer.write("");
    SchemaGenerator.writeMapSchema(writer, context, shape);
  }
}
