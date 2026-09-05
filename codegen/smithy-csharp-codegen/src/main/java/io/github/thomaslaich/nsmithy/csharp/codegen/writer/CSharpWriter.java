/*
 * Specialized SymbolWriter for C#. Mirrors PythonWriter / TypeScriptWriter:
 * supports the $T formatter for Symbol references, exposes addImport(String)
 * for raw namespace imports, and emits a generated-code banner plus collected
 * `using` directives via ImportDeclarations.
 *
 * $T behaviour:
 *  - Symbol with empty namespace: bare name (used for C# keyword primitives
 *    like `string`, `int`, etc.).
 *  - Local, unshadowed types use their short name. Other types use global:: qualification.
 *  - Explicit SymbolReference aliases are preserved.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.writer;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.function.BiFunction;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolReference;
import software.amazon.smithy.codegen.core.SymbolWriter;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpWriter extends SymbolWriter<CSharpWriter, ImportDeclarations> {

  private final String namespace;
  // These generated nested types, members, and type parameters can hide model types.
  private final Set<String> reservedNames =
      new HashSet<>(
          Set.of(
              "Builder", "ValueSerializer", "Schema", "Unknown", "Value", "Tag", "T", "TWriter"));

  public CSharpWriter(String namespace) {
    super(new ImportDeclarations(namespace));
    this.namespace = namespace;
    trimBlankLines();
    trimTrailingSpaces();
    putFormatter('T', new CSharpSymbolFormatter());
  }

  /** Add a raw C# namespace as a `using` directive. */
  public CSharpWriter addImport(String csharpNamespace) {
    getImportContainer().importNamespace(csharpNamespace);
    return this;
  }

  /** Reserve a generated identifier that can hide a type reference in this file. */
  public void reserveName(String name) {
    reservedNames.add(name);
  }

  /** Reserve model member names before rendering references in the containing file. */
  public void reserveMemberNames(Shape shape) {
    shape
        .members()
        .forEach(
            member -> {
              reservedNames.add(CSharpNaming.propertyName(member.getMemberName()));
              reservedNames.add(CSharpNaming.typeName(member.getMemberName()));
            });
  }

  /** Render a type reference without importing potentially ambiguous model namespaces. */
  public String typeName(Symbol symbol) {
    return typeName(symbol, "");
  }

  /** The suffix refers to a generated companion type, such as a shape's schema class. */
  public String typeName(Symbol symbol, String suffix) {
    String name = symbol.getName() + suffix;
    String ns = symbol.getNamespace();
    if (ns.isEmpty()) return name;
    if (ns.equals(namespace) && !reservedNames.contains(name)) return name;
    return "global::" + ns + "." + name;
  }

  /** Emits a C# XML documentation summary from a Smithy shape's documentation trait. */
  public void writeXmlDocs(Shape shape) {
    shape.getTrait(DocumentationTrait.class).ifPresent(trait -> writeXmlDocs(trait.getValue()));
  }

  /**
   * Emits a C# XML documentation summary and parameter tags. Parameter names must match the
   * generated C# declaration.
   */
  public void writeXmlDocs(Shape shape, Map<String, String> parameterDocs) {
    shape
        .getTrait(DocumentationTrait.class)
        .ifPresentOrElse(
            trait -> writeXmlDocs(trait.getValue(), parameterDocs),
            () -> writeXmlDocs((String) null, parameterDocs));
  }

  /** Emits C# XML documentation parameter tags without a summary. */
  public void writeXmlParamDocs(Map<String, String> parameterDocs) {
    writeXmlDocs((String) null, parameterDocs);
  }

  private void writeXmlDocs(String summary) {
    writeXmlDocs(summary, Map.of());
  }

  public void writeXmlDocs(String summary, Map<String, String> parameterDocs) {
    boolean hasSummary = summary != null && !summary.isBlank();
    if (hasSummary) {
      write("/// <summary>");
      writeXmlText(summary);
      write("/// </summary>");
    }
    parameterDocs.forEach(
        (name, documentation) -> {
          if (documentation == null || documentation.isBlank()) {
            return;
          }
          write("/// <param name=\"$L\">", escapeXml(name));
          writeXmlText(documentation);
          write("/// </param>");
        });
  }

  private void writeXmlText(String text) {
    for (String line : text.strip().split("\\R", -1)) {
      if (line.isBlank()) {
        write("///");
      } else {
        write("/// $L", escapeXml(line.strip()));
      }
    }
  }

  private static String escapeXml(String value) {
    return value
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace("\"", "&quot;");
  }

  /** Factory used by WriterDelegator. */
  public static final class CSharpWriterFactory implements SymbolWriter.Factory<CSharpWriter> {
    @Override
    public CSharpWriter apply(String filename, String namespace) {
      return new CSharpWriter(namespace);
    }
  }

  @Override
  public String toString() {
    StringBuilder sb = new StringBuilder();
    sb.append("// <auto-generated />\n");
    sb.append("// Generated by smithy-csharp-codegen. DO NOT EDIT.\n");
    sb.append("#nullable enable\n\n");
    sb.append(getImportContainer().toString());
    sb.append("namespace ").append(namespace).append(";\n\n");
    sb.append(super.toString());
    return sb.toString();
  }

  private final class CSharpSymbolFormatter implements BiFunction<Object, String, String> {
    @Override
    public String apply(Object type, String indent) {
      if (type instanceof Symbol s) {
        return typeName(s);
      }
      if (type instanceof SymbolReference ref) {
        addImport(ref.getSymbol(), ref.getAlias(), SymbolReference.ContextOption.USE);
        return ref.getAlias();
      }
      throw new CodegenException("Invalid type for $T: " + type);
    }
  }
}
