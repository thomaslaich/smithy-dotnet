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
 *  - Framework references collect imports and fall back to global:: qualification on collision.
 *  - Explicit SymbolReference aliases are preserved.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.writer;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;
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
  private final Set<String> modelNames = new HashSet<>();
  private final Map<FrameworkReference, String> frameworkReferences = new LinkedHashMap<>();

  private record FrameworkReference(String qualifiedName, boolean attribute) {}

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

  /** Include declarations that can shadow framework types even when this file never uses them. */
  private void reserveModelNames(Model model, CSharpSettings settings) {
    model
        .shapes()
        .filter(shape -> !shape.isMemberShape())
        .forEach(
            shape -> {
              String ns = settings.csharpNamespace(shape.getId().getNamespace());
              // Namespace segments can also hide imported types during C# name lookup.
              modelNames.addAll(java.util.List.of(ns.split("\\.")));
              if (ns.equals(namespace) || namespace.startsWith(ns + ".")) {
                String name = CSharpNaming.typeName(shape.getId().getName());
                modelNames.add(name);
                modelNames.add(name + "Schema");
                if (shape.isServiceShape()) {
                  modelNames.add(name + "Client");
                  modelNames.add(name + "ClientConfig");
                }
              }
            });
  }

  /**
   * Return a stable reference token. Resolve it after the whole file has registered its names, so
   * import decisions do not depend on emission order. Only explicit tokens are substituted;
   * documentation and model string literals are never simplified.
   */
  public String frameworkType(String qualifiedName) {
    return frameworkReference(qualifiedName, false);
  }

  /** Render an attribute with its conventional short form when that form is unambiguous. */
  public String frameworkAttribute(String qualifiedName) {
    return frameworkReference(qualifiedName, true);
  }

  private String frameworkReference(String qualifiedName, boolean attribute) {
    return frameworkReferences.computeIfAbsent(
        new FrameworkReference(qualifiedName, attribute),
        ignored -> "\u0001framework" + frameworkReferences.size() + "\u0002");
  }

  private String renderFrameworkReferences(String body, Set<String> imports) {
    for (var reference : frameworkReferences.entrySet()) {
      String qualified = reference.getKey().qualifiedName();
      int separator = qualified.lastIndexOf('.');
      String ns = qualified.substring(0, separator);
      String name = qualified.substring(separator + 1);
      String shortName =
          reference.getKey().attribute() && name.endsWith("Attribute")
              ? name.substring(0, name.length() - 9)
              : name;
      boolean collision =
          reservedNames.contains(name)
              || modelNames.contains(name)
              || reservedNames.contains(shortName)
              || modelNames.contains(shortName)
              || frameworkReferences.keySet().stream()
                  .anyMatch(
                      other ->
                          !other.qualifiedName().equals(qualified)
                              && other.qualifiedName().endsWith("." + name));
      String rendered = "global::" + qualified;
      if (!collision) {
        imports.add(ns);
        rendered = shortName;
      }
      body = body.replace(reference.getValue(), rendered);
    }
    return body;
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
    if (symbol.getDefinitionFile().isEmpty() && (ns.equals("System") || ns.startsWith("System."))) {
      return frameworkType(ns + "." + name);
    }
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
    Set<String> frameworkImports = new HashSet<>();
    String body = renderFrameworkReferences(super.toString(), frameworkImports);
    StringBuilder sb = new StringBuilder();
    sb.append("// <auto-generated />\n");
    sb.append("// Generated by smithy-csharp-codegen. DO NOT EDIT.\n");
    sb.append("#nullable enable\n\n");
    sb.append(getImportContainer().render(frameworkImports));
    sb.append("namespace ").append(namespace).append(";\n\n");
    sb.append(body);
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
