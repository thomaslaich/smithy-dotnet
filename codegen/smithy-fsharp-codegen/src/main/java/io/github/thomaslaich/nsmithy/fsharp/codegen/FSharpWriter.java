/*
 * Writer for one section of a generated F# file. It renders every type it references with a
 * global-qualified name, so a model type can never hide one F# or the runtime declares and the
 * file needs no `open` declarations.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.XmlDocMarkup;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
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

  /**
   * Writes a template whose named parameters come from {@code context}, in a state of its own so
   * the parameters do not leak into what the writer writes next.
   */
  public FSharpWriter writeTemplate(String template, Map<String, ?> context) {
    pushState();
    try {
      context.forEach(this::putContext);
      write(template);
    } finally {
      popState();
    }
    return this;
  }

  /** The code a template renders to, for a value another template writes with {@code |}. */
  public static String render(String template, Map<String, ?> context) {
    String code = new FSharpWriter().writeTemplate(template, context).toString();
    return code.endsWith("\n") ? code.substring(0, code.length() - 1) : code;
  }

  /** Writes a shape's documentation as an XML doc summary. */
  public void writeDocs(Shape shape) {
    writeRenderedDocs(docs(shape));
  }

  public void writeDocs(String documentation) {
    writeRenderedDocs(docs(documentation));
  }

  private void writeRenderedDocs(String docs) {
    if (!docs.isEmpty()) {
      write("$L", docs);
    }
  }

  /**
   * A shape's documentation as an XML doc summary, or an empty string, which a template's {@code
   * ${?docs}} skips.
   */
  public static String docs(Shape shape) {
    return shape.getTrait(DocumentationTrait.class).map(t -> docs(t.getValue())).orElse("");
  }

  public static String docs(String documentation) {
    if (documentation == null || documentation.isBlank()) {
      return "";
    }
    List<String> lines = new ArrayList<>();
    lines.add("/// <summary>");
    for (String line : XmlDocMarkup.render(documentation)) {
      lines.add(line.isEmpty() ? "///" : "/// " + line);
    }
    lines.add("/// </summary>");
    return String.join("\n", lines);
  }

  /** Code with its documentation, if any, on the lines before it. */
  public static String documented(String docs, String code) {
    return docs.isEmpty() ? code : docs + "\n" + code;
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
