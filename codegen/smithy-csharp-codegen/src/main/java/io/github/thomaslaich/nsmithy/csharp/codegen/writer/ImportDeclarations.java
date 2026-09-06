/* Tracks C# symbol references and their using directives. */
package io.github.thomaslaich.nsmithy.csharp.codegen.writer;

import io.github.thomaslaich.nsmithy.csharp.codegen.SymbolProperties;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import software.amazon.smithy.codegen.core.ImportContainer;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ImportDeclarations implements ImportContainer {
  private final String currentNamespace;
  private final Set<String> imports = new TreeSet<>();
  private final Map<Reference, String> references = new LinkedHashMap<>();
  // Shape names and generated companions visible in the current or a parent namespace.
  private final Set<String> modelTypeNames = new HashSet<>();
  // Conservatively reserve segments from every model namespace, including unrelated ones.
  private final Set<String> namespaceSegments = new HashSet<>();
  private final Set<String> reservedNames =
      new HashSet<>(
          Set.of(
              "Builder", "ValueSerializer", "Schema", "Unknown", "Value", "Tag", "T", "TWriter"));

  public ImportDeclarations(String currentNamespace) {
    this.currentNamespace = currentNamespace;
  }

  @Override
  public void importSymbol(Symbol symbol, String alias) {
    references.computeIfAbsent(
        new Reference(symbol, alias), ignored -> "\u0001symbol" + references.size() + "\u0002");
  }

  /** Register a symbol now; decide its spelling once all file names are known. */
  String reference(Symbol symbol, String alias) {
    importSymbol(symbol, alias);
    return references.get(new Reference(symbol, alias));
  }

  void reserveName(String name) {
    reservedNames.add(name);
  }

  void reserveModelTypeName(String name) {
    modelTypeNames.add(name);
  }

  void reserveNamespaceSegment(String name) {
    namespaceSegments.add(name);
  }

  /** Import a raw C# namespace, including extension methods with no type reference. */
  public void importNamespace(String namespace) {
    if (namespace != null && !namespace.isEmpty() && !namespace.equals(currentNamespace)) {
      imports.add(namespace);
    }
  }

  String renderReferences(String body) {
    for (var entry : references.entrySet()) {
      Reference reference = entry.getKey();
      String rendered = reference.shortName();
      if (usesQualifiedName(reference)) {
        rendered = "global::" + reference.symbol().getFullName();
      }
      body = body.replace(entry.getValue(), rendered);
    }
    return body;
  }

  @Override
  public String toString() {
    Set<String> declarations = new TreeSet<>();
    for (String namespace : imports) {
      declarations.add(namespace);
    }
    for (Reference reference : references.keySet()) {
      Symbol symbol = reference.symbol();
      if (symbol.getNamespace().isEmpty() || usesQualifiedName(reference)) continue;
      if (reference.isAlias()) {
        declarations.add(reference.alias() + " = global::" + symbol.getFullName());
      } else if (!symbol.getNamespace().equals(currentNamespace)) {
        declarations.add(symbol.getNamespace());
      }
    }
    return declarations.isEmpty()
        ? ""
        : "using " + String.join(";\nusing ", declarations) + ";\n\n";
  }

  private boolean isReservedName(String name) {
    return reservedNames.contains(name);
  }

  /** Check each source of shadowing for both the full name and attribute shorthand. */
  private boolean hasNameCollision(Reference reference) {
    String name = reference.alias();
    String shortName = reference.shortName();
    if (reservedNames.contains(name) || reservedNames.contains(shortName)) return true;
    if (modelTypeNames.contains(name) || modelTypeNames.contains(shortName)) return true;
    if (namespaceSegments.contains(name) || namespaceSegments.contains(shortName)) return true;
    return references.keySet().stream()
        .anyMatch(
            other ->
                !other.symbol().getFullName().equals(reference.symbol().getFullName())
                    && (other.alias().equals(name) || other.shortName().equals(shortName)));
  }

  private boolean usesQualifiedName(Reference reference) {
    Symbol symbol = reference.symbol();
    String namespace = symbol.getNamespace();
    if (namespace.isEmpty()) return false;
    if (reference.isAlias()) return hasNameCollision(reference);
    // Keep model references qualified outside their own namespace.
    // External symbols can use namespace imports when their names are unambiguous.
    if (!symbol.getDefinitionFile().isEmpty()) {
      return !namespace.equals(currentNamespace) || isReservedName(symbol.getName());
    }
    return hasNameCollision(reference);
  }

  private record Reference(Symbol symbol, String alias) {
    boolean isAlias() {
      return !alias.equals(symbol.getName());
    }

    String shortName() {
      if (!isAlias()
          && symbol.getProperty(SymbolProperties.IS_ATTRIBUTE, Boolean.class).orElse(false)
          && alias.endsWith("Attribute")) {
        return alias.substring(0, alias.length() - 9);
      }
      return alias;
    }
  }
}
