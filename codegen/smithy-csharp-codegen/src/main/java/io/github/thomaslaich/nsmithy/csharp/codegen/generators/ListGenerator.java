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

    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      writer.putContext("memberType", memberType);
      writer.putContext("enumerable", RuntimeTypes.I_ENUMERABLE);
      writer.putContext("list", RuntimeTypes.LIST);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext("array", RuntimeTypes.ARRAY);
      writer.putContext("enumerableMethods", RuntimeTypes.ENUMERABLE);
      writer.putContext(
          "valuesProperty",
          writer.consumer(
              w -> {
                w.writeXmlDocs(shape.getMember());
                w.write("public $T<$L> Values { get; }", RuntimeTypes.I_READ_ONLY_LIST, memberType);
              }));
      writer.writeXmlDocs(shape);
      writer.write(
          """
          public sealed partial record class ${typeName:L}
          {
              public ${typeName:L}(${enumerable:T}<${memberType:L}> values)
              {
                  ${argumentNullException:T}.ThrowIfNull(values);
                  Values = ${array:T}.AsReadOnly(${enumerableMethods:T}.ToArray(values));
              }

              private ${typeName:L}(${list:T}<${memberType:L}> values)
              {
                  Values = values.AsReadOnly();
              }

              internal static ${typeName:L} FromOwnedList(${list:T}<${memberType:L}> values) =>
                  new(values);

              ${valuesProperty:C|}
          }
          """);
    } finally {
      writer.popState();
    }
    writer.write("");
    SchemaGenerator.writeListSchema(writer, context, shape);
  }
}
