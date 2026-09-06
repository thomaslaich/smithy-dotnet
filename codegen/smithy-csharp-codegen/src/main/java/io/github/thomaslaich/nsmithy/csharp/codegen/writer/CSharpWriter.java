/*
 * Specialized SymbolWriter for C#. Mirrors PythonWriter / TypeScriptWriter:
 * supports the $T formatter for Symbol references, exposes addImport(String)
 * for raw namespace imports, and emits a generated-code banner plus collected
 * `using` directives via ImportDeclarations.
 *
 * $T behaviour:
 *  - Symbol with empty namespace: bare name (used for C# keyword primitives
 *    like `string`, `int`, etc.).
 *  - Local, unshadowed model types use their short name; other model types are qualified.
 *  - External references collect imports and fall back to global:: qualification on collision.
 *  - Explicit SymbolReference aliases are preserved.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.writer;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.SymbolProperties;
import java.util.Map;
import java.util.function.BiFunction;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolReference;
import software.amazon.smithy.codegen.core.SymbolWriter;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpWriter extends SymbolWriter<CSharpWriter, ImportDeclarations> {

  private final String namespace;

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
    getImportContainer().reserveName(name);
  }

  /** Render an attribute symbol using its conventional shorthand when safe. */
  public String attributeName(Symbol symbol) {
    return typeName(symbol.toBuilder().putProperty(SymbolProperties.IS_ATTRIBUTE, true).build());
  }

  /** Reserve model member names before rendering references in the containing file. */
  public void reserveMemberNames(Shape shape) {
    shape
        .members()
        .forEach(
            member -> {
              getImportContainer().reserveName(CSharpNaming.propertyName(member.getMemberName()));
              getImportContainer().reserveName(CSharpNaming.typeName(member.getMemberName()));
            });
  }

  /** Render a type reference without importing potentially ambiguous model namespaces. */
  public String typeName(Symbol symbol) {
    return typeName(symbol, "");
  }

  /** The suffix refers to a generated companion type, such as a shape's schema class. */
  public String typeName(Symbol symbol, String suffix) {
    Symbol reference =
        suffix.isEmpty() ? symbol : symbol.toBuilder().name(symbol.getName() + suffix).build();
    addUseImports(reference);
    return getImportContainer().reference(reference, reference.getName());
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

  /** Factory used by WriterDelegator. */
  public static final class CSharpWriterFactory implements SymbolWriter.Factory<CSharpWriter> {
    private final Model model;
    private final CSharpSettings settings;

    public CSharpWriterFactory(Model model, CSharpSettings settings) {
      this.model = model;
      this.settings = settings;
    }

    @Override
    public CSharpWriter apply(String filename, String namespace) {
      var writer = new CSharpWriter(namespace);
      writer.reserveModelNames(model, settings);
      return writer;
    }
  }

  @Override
  public String toString() {
    String body = getImportContainer().renderReferences(super.toString());
    StringBuilder sb = new StringBuilder();
    sb.append("// <auto-generated />\n");
    sb.append("// Generated by smithy-csharp-codegen. DO NOT EDIT.\n");
    sb.append("#nullable enable\n\n");
    sb.append(getImportContainer().toString());
    sb.append("namespace ").append(namespace).append(";\n\n");
    sb.append(body);
    return sb.toString();
  }

  /** Include declarations that can shadow framework types even when this file never uses them. */
  private void reserveModelNames(Model model, CSharpSettings settings) {
    model
        .shapes()
        .filter(shape -> !shape.isMemberShape())
        .forEach(
            shape -> {
              String ns = settings.csharpNamespace(shape.getId().getNamespace());
              // Namespace segments can also hide imported types during C# name lookup.
              for (String segment : ns.split("\\.")) {
                getImportContainer().reserveNamespaceSegment(segment);
              }
              if (ns.equals(namespace) || namespace.startsWith(ns + ".")) {
                String name = CSharpNaming.typeName(shape.getId().getName());
                getImportContainer().reserveModelTypeName(name);
                getImportContainer().reserveModelTypeName(name + "Schema");
                if (shape.isServiceShape()) {
                  getImportContainer().reserveModelTypeName(name + "Client");
                  getImportContainer().reserveModelTypeName(name + "ClientConfig");
                }
              }
            });
  }

  private void writeXmlDocs(String summary) {
    writeXmlDocs(summary, Map.of());
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

  private final class CSharpSymbolFormatter implements BiFunction<Object, String, String> {
    @Override
    public String apply(Object type, String indent) {
      if (type instanceof Symbol s) {
        return typeName(s);
      }
      if (type instanceof SymbolReference ref) {
        addImport(ref.getSymbol(), ref.getAlias(), SymbolReference.ContextOption.USE);
        return getImportContainer().reference(ref.getSymbol(), ref.getAlias());
      }
      throw new CodegenException("Invalid type for $T: " + type);
    }
  }
}
