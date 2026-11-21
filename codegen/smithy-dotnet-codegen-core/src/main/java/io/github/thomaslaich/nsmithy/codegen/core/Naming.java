package io.github.thomaslaich.nsmithy.codegen.core;

import software.amazon.smithy.utils.SmithyInternalApi;

/** Casing and namespace mapping shared by the .NET languages. */
@SmithyInternalApi
public final class Naming {

  private Naming() {}

  /** Smithy namespace -> .NET namespace, prefixed with optional baseNamespace. */
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

  /**
   * An enum member name in Pascal case: like {@link #pascal}, but a word written in capitals keeps
   * only its first capital, so {@code IN_PROGRESS} becomes {@code InProgress}.
   */
  public static String enumValue(String value) {
    StringBuilder sb = new StringBuilder(value.length());
    for (String word : value.split("[^\\p{L}\\p{N}]+")) {
      boolean capitals = word.codePoints().noneMatch(Character::isLowerCase);
      sb.append(capitals ? word.toLowerCase(java.util.Locale.ROOT) : word).append(' ');
    }
    return pascal(sb.toString());
  }
}
