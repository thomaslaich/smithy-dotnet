/*
 * Identifier and casing utilities. Port of NSmithy.CodeGeneration.CSharp.CSharpIdentifier.
 */
package io.github.thomaslaich.smithy.csharp.codegen;

import java.util.Set;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class CSharpNaming {

  private static final Set<String> KEYWORDS =
      Set.of(
          "abstract",
          "as",
          "base",
          "bool",
          "break",
          "byte",
          "case",
          "catch",
          "char",
          "checked",
          "class",
          "const",
          "continue",
          "decimal",
          "default",
          "delegate",
          "do",
          "double",
          "else",
          "enum",
          "event",
          "explicit",
          "extern",
          "false",
          "finally",
          "fixed",
          "float",
          "for",
          "foreach",
          "goto",
          "if",
          "implicit",
          "in",
          "int",
          "interface",
          "internal",
          "is",
          "lock",
          "long",
          "namespace",
          "new",
          "null",
          "object",
          "operator",
          "out",
          "override",
          "params",
          "private",
          "protected",
          "public",
          "readonly",
          "ref",
          "return",
          "sbyte",
          "sealed",
          "short",
          "sizeof",
          "stackalloc",
          "static",
          "string",
          "struct",
          "switch",
          "this",
          "throw",
          "true",
          "try",
          "typeof",
          "uint",
          "ulong",
          "unchecked",
          "unsafe",
          "ushort",
          "using",
          "virtual",
          "void",
          "volatile",
          "while");

  private CSharpNaming() {}

  /** Smithy namespace -> C# namespace, prefixed with optional baseNamespace. */
  public static String namespaceFor(String smithyNamespace, String baseNamespace) {
    StringBuilder sb = new StringBuilder();
    boolean first = true;
    for (String part : smithyNamespace.split("\\.")) {
      if (!first) {
        sb.append('.');
      }
      sb.append(pascal(part));
      first = false;
    }
    String suffix = sb.toString();
    if (baseNamespace == null || baseNamespace.isBlank()) {
      return suffix;
    }
    return baseNamespace + "." + suffix;
  }

  public static String typeName(String value) {
    return escapeIfNeeded(pascal(value));
  }

  public static String propertyName(String value) {
    return escapeIfNeeded(pascal(value));
  }

  public static String parameterName(String value) {
    String camel = camel(value);
    return KEYWORDS.contains(camel) ? "@" + camel : camel;
  }

  public static String fileSegment(String value) {
    return pascal(value);
  }

  public static String pascal(String value) {
    StringBuilder sb = new StringBuilder(value.length());
    boolean capitalize = true;
    for (int i = 0; i < value.length(); ) {
      int cp = value.codePointAt(i);
      int width = Character.charCount(cp);
      if (!Character.isLetterOrDigit(cp)) {
        capitalize = true;
        i += width;
        continue;
      }
      if (sb.length() == 0 && Character.isDigit(cp)) {
        sb.append('_');
      }
      String s = new String(Character.toChars(cp));
      sb.append(capitalize ? s.toUpperCase(java.util.Locale.ROOT) : s);
      capitalize = false;
      i += width;
    }
    return sb.length() == 0 ? "_" : sb.toString();
  }

  public static String camel(String value) {
    String p = pascal(value);
    if (p.length() <= 1) {
      return p.toLowerCase(java.util.Locale.ROOT);
    }
    return Character.toLowerCase(p.charAt(0)) + p.substring(1);
  }

  private static String escapeIfNeeded(String v) {
    return KEYWORDS.contains(v) ? "@" + v : v;
  }

  /** Escape a Java string into a C# string literal, including surrounding quotes. */
  public static String formatString(String value) {
    StringBuilder sb = new StringBuilder(value.length() + 2);
    sb.append('"');
    for (int i = 0; i < value.length(); i++) {
      char c = value.charAt(i);
      switch (c) {
        case '\\' -> sb.append("\\\\");
        case '"' -> sb.append("\\\"");
        case '\0' -> sb.append("\\0");
        case '\b' -> sb.append("\\b");
        case '\f' -> sb.append("\\f");
        case '\n' -> sb.append("\\n");
        case '\r' -> sb.append("\\r");
        case '\t' -> sb.append("\\t");
        default -> sb.append(c);
      }
    }
    sb.append('"');
    return sb.toString();
  }
}
