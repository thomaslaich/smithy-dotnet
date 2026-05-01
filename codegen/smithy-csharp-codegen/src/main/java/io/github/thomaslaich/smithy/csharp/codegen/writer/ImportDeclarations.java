/*
 * Tracks `using` directives for a generated C# file. Implements ImportContainer
 * so it composes with SymbolWriter the same way ImportDeclarations does in
 * smithy-python / smithy-typescript.
 *
 * In addition to symbol-driven imports it also accepts raw namespace strings,
 * which generators use for runtime namespaces that have no Smithy symbol
 * (e.g. NSmithy.Server.AspNetCore, Microsoft.AspNetCore.Builder).
 */
package io.github.thomaslaich.smithy.csharp.codegen.writer;

import java.util.Set;
import java.util.TreeSet;
import software.amazon.smithy.codegen.core.ImportContainer;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ImportDeclarations implements ImportContainer {

  private final String currentNamespace;
  private final Set<String> imports = new TreeSet<>();

  public ImportDeclarations(String currentNamespace) {
    this.currentNamespace = currentNamespace;
  }

  @Override
  public void importSymbol(Symbol symbol, String alias) {
    importNamespace(symbol.getNamespace());
  }

  /** Import a raw C# namespace (no symbol). */
  public void importNamespace(String namespace) {
    if (namespace == null || namespace.isEmpty() || namespace.equals(currentNamespace)) {
      return;
    }
    imports.add(namespace);
  }

  @Override
  public String toString() {
    if (imports.isEmpty()) {
      return "";
    }
    StringBuilder sb = new StringBuilder();
    for (String ns : imports) {
      sb.append("using ").append(ns).append(";\n");
    }
    sb.append('\n');
    return sb.toString();
  }
}
