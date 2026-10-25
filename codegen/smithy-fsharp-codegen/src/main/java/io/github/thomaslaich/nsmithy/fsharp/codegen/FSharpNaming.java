package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.Naming;
import java.util.Set;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.utils.SmithyInternalApi;

/** F# identifiers for shapes and members, and F# literals. */
@SmithyInternalApi
public final class FSharpNaming {

  /** F# keywords and reserved identifiers, which an identifier spells in double backticks. */
  private static final Set<String> KEYWORDS =
      Set.of(
          "abstract",
          "and",
          "as",
          "assert",
          "base",
          "begin",
          "class",
          "default",
          "delegate",
          "do",
          "done",
          "downcast",
          "downto",
          "elif",
          "else",
          "end",
          "exception",
          "extern",
          "false",
          "finally",
          "fixed",
          "for",
          "fun",
          "function",
          "global",
          "if",
          "in",
          "inherit",
          "inline",
          "interface",
          "internal",
          "lazy",
          "let",
          "match",
          "member",
          "module",
          "mutable",
          "namespace",
          "new",
          "not",
          "null",
          "of",
          "open",
          "or",
          "override",
          "private",
          "public",
          "rec",
          "return",
          "select",
          "sig",
          "static",
          "struct",
          "then",
          "to",
          "true",
          "try",
          "type",
          "upcast",
          "use",
          "val",
          "void",
          "when",
          "while",
          "with",
          "yield",
          "const",
          "break",
          "checked",
          "component",
          "constraint",
          "continue",
          "event",
          "external",
          "include",
          "mixin",
          "parallel",
          "process",
          "protected",
          "pure",
          "sealed",
          "tailcall",
          "trait",
          "virtual",
          "asr",
          "land",
          "lor",
          "lsl",
          "lsr",
          "lxor",
          "mod");

  /** Members every generated type inherits from {@code obj}, which a member cannot reuse. */
  private static final Set<String> INHERITED_MEMBERS =
      Set.of("Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone", "Finalize");

  /** Members an exception inherits, which a generated error's property cannot reuse. */
  private static final Set<String> EXCEPTION_MEMBERS =
      Set.of(
          "Message",
          "Data",
          "Source",
          "StackTrace",
          "InnerException",
          "HelpLink",
          "HResult",
          "TargetSite",
          "GetBaseException",
          "GetObjectData",
          "ErrorMessage");

  /**
   * Names a generated union or enum declares besides its cases ({@code Unknown}, and an enum's
   * {@code Value} and {@code FromValue}), plus the {@code Tag} property F# compiles into every
   * union.
   */
  private static final Set<String> CASE_RESERVED = Set.of("Unknown", "Value", "FromValue", "Tag");

  private static final String COLLISION_SUFFIX = "Value";
  private static final String CASE_COLLISION_SUFFIX = "Case";

  /** The property that holds an error's modeled message. */
  public static final String ERROR_MESSAGE = "ErrorMessage";

  private FSharpNaming() {}

  public static String typeName(String shapeName) {
    String name = Naming.pascal(shapeName);
    return INHERITED_MEMBERS.contains(name) ? name + COLLISION_SUFFIX : name;
  }

  /** A record field, builder property, or error property. */
  public static String fieldName(MemberShape member) {
    return escape(avoid(Naming.pascal(member.getMemberName()), INHERITED_MEMBERS));
  }

  /** An error's property, which also avoids the members an exception inherits. */
  public static String errorPropertyName(MemberShape member) {
    String name = avoid(Naming.pascal(member.getMemberName()), INHERITED_MEMBERS);
    return escape(EXCEPTION_MEMBERS.contains(name) ? name + COLLISION_SUFFIX : name);
  }

  /** An error constructor parameter. */
  public static String parameterName(String propertyName) {
    return escape(Naming.camel(unescape(propertyName)));
  }

  /** A union case or string enum case. */
  public static String caseName(MemberShape member) {
    String name = caseIdentifier(member.getMemberName());
    boolean collides =
        INHERITED_MEMBERS.contains(name)
            || CASE_RESERVED.contains(name)
            || name.equals(typeName(member.getContainer().getName()));
    return collides ? name + CASE_COLLISION_SUFFIX : name;
  }

  /** An int enum's named value. */
  public static String intEnumValueName(MemberShape member) {
    return caseIdentifier(member.getMemberName());
  }

  /** Spells {@code value} as an F# string literal, including the quotes. */
  public static String stringLiteral(String value) {
    StringBuilder sb = new StringBuilder(value.length() + 2);
    sb.append('"');
    for (int i = 0; i < value.length(); i++) {
      char c = value.charAt(i);
      switch (c) {
        case '\\' -> sb.append("\\\\");
        case '"' -> sb.append("\\\"");
        case '\n' -> sb.append("\\n");
        case '\r' -> sb.append("\\r");
        case '\t' -> sb.append("\\t");
        case '\b' -> sb.append("\\b");
        default -> {
          if (Character.isISOControl(c) || c == ' ' || c == ' ') {
            sb.append(String.format("\\u%04X", (int) c));
          } else {
            sb.append(c);
          }
        }
      }
    }
    return sb.append('"').toString();
  }

  /**
   * A case identifier: union cases must start with an upper-case letter, so a name that starts with
   * a digit gets a letter in front of it.
   */
  private static String caseIdentifier(String value) {
    String name = Naming.pascal(value);
    if (name.startsWith("_")) {
      name = "N" + name.substring(1);
    }
    return name;
  }

  private static String avoid(String name, Set<String> taken) {
    return taken.contains(name) ? name + COLLISION_SUFFIX : name;
  }

  private static String escape(String name) {
    return KEYWORDS.contains(name) ? "``" + name + "``" : name;
  }

  private static String unescape(String name) {
    return name.startsWith("``") ? name.substring(2, name.length() - 2) : name;
  }
}
