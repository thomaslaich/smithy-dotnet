/*
 * Symbol property keys attached by CSharpSymbolProvider. Mirrors
 * software.amazon.smithy.python.codegen.SymbolProperties.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class SymbolProperties {

  /** Marks a symbol as a C# value type (struct/primitive). */
  public static final String IS_VALUE_TYPE = "isValueType";

  /** Marks a type reference used as a C# attribute, permitting the short attribute spelling. */
  public static final String IS_ATTRIBUTE = "isAttribute";

  /**
   * The generic type arguments of a constructed type such as {@code IReadOnlyList<T>}, as a {@code
   * List<Symbol>} rendered between angle brackets after the symbol's name.
   */
  public static final String TYPE_ARGUMENTS = "typeArguments";

  /** Marks a type argument as a nullable reference or value ({@code T?}). */
  public static final String IS_NULLABLE = "isNullable";

  private SymbolProperties() {}
}
