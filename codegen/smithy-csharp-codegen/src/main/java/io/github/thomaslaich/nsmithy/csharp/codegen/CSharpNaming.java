/*
 * Identifier and casing utilities. Port of NSmithy.CodeGeneration.CSharp.CSharpIdentifier.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.Naming;
import java.util.Set;
import software.amazon.smithy.model.shapes.MemberShape;
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

  /**
   * Members every generated type inherits from {@code object} or that C# synthesizes for a record,
   * so a generated member or type of the same name would clash with them.
   */
  private static final Set<String> INHERITED_MEMBERS =
      Set.of(
          "Equals",
          "GetHashCode",
          "ToString",
          "GetType",
          "MemberwiseClone",
          "Finalize",
          "ReferenceEquals",
          "EqualityContract",
          "PrintMembers",
          "Deconstruct");

  /** Names every generated union already declares, which a variant class cannot reuse. */
  private static final Set<String> UNION_MEMBERS = Set.of("Value", "Tag", "Unknown", "Match");

  /** Suffix for a member, enum value, or type whose name would clash. */
  private static final String COLLISION_SUFFIX = "Value";

  /** Suffix for a union variant class whose name would clash. */
  private static final String VARIANT_COLLISION_SUFFIX = "Case";

  private CSharpNaming() {}

  public static String typeName(String value) {
    return escapeIfNeeded(avoidInheritedMember(Naming.pascal(value)));
  }

  public static String propertyName(String value) {
    return escapeIfNeeded(avoidInheritedMember(Naming.pascal(value)));
  }

  /**
   * The property, constructor parameter, or enum value name of a structure, error, or enum member.
   * C# forbids a member named like its enclosing type, so that one takes the collision suffix.
   */
  public static String memberName(MemberShape member) {
    String name = propertyName(member.getMemberName());
    return name.equals(typeName(member.getContainer().getName())) ? name + COLLISION_SUFFIX : name;
  }

  /**
   * A string enum value: a static property on a record struct that also declares {@code Value} and
   * {@code FromValue}, so a member with one of those names takes the collision suffix too.
   */
  public static String stringEnumValueName(MemberShape member) {
    String name = memberName(member);
    return name.equals("Value") || name.equals("FromValue") ? name + COLLISION_SUFFIX : name;
  }

  /**
   * The nested class of a union variant. It cannot reuse the union's own name, the names every
   * union declares ({@code Value}, {@code Tag}, {@code Unknown}, {@code Match}), or an inherited
   * member, so a variant with one of those names takes the variant collision suffix.
   */
  public static String variantName(MemberShape member) {
    String name = Naming.pascal(member.getMemberName());
    boolean collides =
        INHERITED_MEMBERS.contains(name)
            || UNION_MEMBERS.contains(name)
            || name.equals(typeName(member.getContainer().getName()));
    return escapeIfNeeded(collides ? name + VARIANT_COLLISION_SUFFIX : name);
  }

  /** The static factory that creates a union variant, named after the member as modeled. */
  public static String variantFactoryName(MemberShape member) {
    return "From" + Naming.pascal(member.getMemberName());
  }

  private static String avoidInheritedMember(String name) {
    return INHERITED_MEMBERS.contains(name) ? name + COLLISION_SUFFIX : name;
  }

  public static String parameterName(String value) {
    String camel = Naming.camel(value);
    return KEYWORDS.contains(camel) ? "@" + camel : camel;
  }

  public static String fileSegment(String value) {
    return Naming.pascal(value);
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
        default -> {
          // C# also ends a line at U+0085, U+2028, and U+2029, so those (and any other
          // control character) must be escaped to keep the literal on one line.
          if (Character.isISOControl(c) || c == '\u2028' || c == '\u2029') {
            sb.append(String.format("\\u%04X", (int) c));
          } else {
            sb.append(c);
          }
        }
      }
    }
    sb.append('"');
    return sb.toString();
  }
}
