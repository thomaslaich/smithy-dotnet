package io.github.thomaslaich.nsmithy.proto.codegen;

import java.util.Locale;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Minimal naming utilities for proto codegen. Intentionally does not depend on {@code
 * smithy-csharp-codegen} so the two plugins remain independently swappable.
 */
@SmithyInternalApi
final class ProtoNaming {

  private ProtoNaming() {}

  /**
   * Converts a Smithy shape name to PascalCase, matching the convention used by {@code
   * smithy-csharp-codegen}. Smithy shape names are already PascalCase by convention, so this mainly
   * guards against edge cases.
   */
  static String typeName(String value) {
    return pascal(value);
  }

  /**
   * Converts a Smithy namespace to a PascalCase namespace, optionally prefixed by {@code
   * baseNamespace}.
   *
   * <p>Example: {@code "example.hello"} → {@code "Example.Hello"}.
   */
  static String namespaceFor(String smithyNamespace, String baseNamespace) {
    StringBuilder sb = new StringBuilder();
    boolean first = true;
    for (String part : smithyNamespace.split("\\.")) {
      if (!first) sb.append('.');
      sb.append(pascal(part));
      first = false;
    }
    String suffix = sb.toString();
    if (baseNamespace == null || baseNamespace.isBlank()) return suffix;
    return baseNamespace + "." + suffix;
  }

  /** Convert an identifier to PascalCase, stripping non-alphanumeric characters. */
  static String pascal(String value) {
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
      if (sb.length() == 0 && Character.isDigit(cp)) sb.append('_');
      String s = new String(Character.toChars(cp));
      sb.append(capitalize ? s.toUpperCase(Locale.ROOT) : s);
      capitalize = false;
      i += width;
    }
    return sb.length() == 0 ? "_" : sb.toString();
  }
}
