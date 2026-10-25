/*
 * Writer for one section of a generated F# file. It renders every type it references with a
 * global-qualified name, so a model type can never hide one F# or the runtime declares and the
 * file needs no `open` declarations.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import java.util.function.BiFunction;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.ImportContainer;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolWriter;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class FSharpWriter extends SymbolWriter<FSharpWriter, FSharpWriter.NoImports> {

  public FSharpWriter() {
    super(new NoImports());
    trimBlankLines();
    trimTrailingSpaces();
    putFormatter('T', new TypeFormatter());
  }

  /**
   * A type reference: a type F# or the runtime declares as its symbol names it, and a generated
   * type qualified from {@code global}.
   */
  public String typeName(Symbol symbol) {
    if (symbol.getNamespace().isEmpty()) {
      return symbol.getName();
    }
    return "global." + symbol.getNamespace() + "." + symbol.getName();
  }

  /** Writes a shape's documentation as an XML doc summary. */
  public void writeDocs(Shape shape) {
    shape.getTrait(DocumentationTrait.class).ifPresent(trait -> writeDocs(trait.getValue()));
  }

  public void writeDocs(String documentation) {
    if (documentation == null || documentation.isBlank()) {
      return;
    }
    write("/// <summary>");
    for (String line : documentation.strip().split("\\R", -1)) {
      if (line.isBlank()) {
        write("///");
      } else {
        write("/// $L", escapeXml(line.strip()));
      }
    }
    write("/// </summary>");
  }

  private static String escapeXml(String value) {
    return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
  }

  /** F# needs no imports: every reference is qualified. */
  public static final class NoImports implements ImportContainer {
    @Override
    public void importSymbol(Symbol symbol, String alias) {}

    @Override
    public String toString() {
      return "";
    }
  }

  private final class TypeFormatter implements BiFunction<Object, String, String> {
    @Override
    public String apply(Object type, String indent) {
      if (type instanceof Symbol symbol) {
        return typeName(symbol);
      }
      throw new CodegenException("Invalid type for $T: " + type);
    }
  }
}
