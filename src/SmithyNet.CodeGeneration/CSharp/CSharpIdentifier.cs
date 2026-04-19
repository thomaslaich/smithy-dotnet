using System.Globalization;
using System.Text;

namespace SmithyNet.CodeGeneration.CSharp;

internal static class CSharpIdentifier
{
    private static readonly HashSet<string> Keywords =
    [
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
        "while",
    ];

    public static string Namespace(string smithyNamespace, string? baseNamespace)
    {
        var suffix = string.Join(".", smithyNamespace.Split('.').Select(PascalCase));
        return string.IsNullOrWhiteSpace(baseNamespace) ? suffix : $"{baseNamespace}.{suffix}";
    }

    public static string TypeName(string value)
    {
        return EscapeIfNeeded(PascalCase(value));
    }

    public static string PropertyName(string value)
    {
        return EscapeIfNeeded(PascalCase(value));
    }

    public static string ParameterName(string value)
    {
        var identifier = CamelCase(value);
        return Keywords.Contains(identifier) ? $"@{identifier}" : identifier;
    }

    public static string FileSegment(string value)
    {
        return PascalCase(value);
    }

    private static string PascalCase(string value)
    {
        var builder = new StringBuilder(value.Length);
        var capitalize = true;
        foreach (var rune in value.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune))
            {
                capitalize = true;
                continue;
            }

            if (builder.Length == 0 && Rune.IsDigit(rune))
            {
                builder.Append('_');
            }

            var text = capitalize
                ? rune.ToString().ToUpper(CultureInfo.InvariantCulture)
                : rune.ToString();
            builder.Append(text);
            capitalize = false;
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static string CamelCase(string value)
    {
        var pascal = PascalCase(value);
        return pascal.Length == 1
            ? pascal.ToLower(CultureInfo.InvariantCulture)
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{pascal[..1].ToLower(CultureInfo.InvariantCulture)}{pascal[1..]}"
            );
    }

    private static string EscapeIfNeeded(string value)
    {
        return Keywords.Contains(value) ? $"@{value}" : value;
    }
}
