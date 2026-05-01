/*
 * Symbol property keys attached by CSharpSymbolProvider. Mirrors
 * software.amazon.smithy.python.codegen.SymbolProperties.
 */
package io.github.thomaslaich.smithy.csharp.codegen;

import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class SymbolProperties {

  /** Marks a symbol as a C# value type (struct/primitive). */
  public static final String IS_VALUE_TYPE = "isValueType";

  private SymbolProperties() {}
}
