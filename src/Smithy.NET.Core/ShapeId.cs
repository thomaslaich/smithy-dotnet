using System.Globalization;

namespace Smithy.NET.Core;

public readonly record struct ShapeId(string Namespace, string Name, string? MemberName = null)
{
    public ShapeId(string @namespace, string name)
        : this(@namespace, name, null) { }

    public bool IsMember => MemberName is not null;

    public static ShapeId Parse(string value)
    {
        if (!TryParse(value, out var shapeId))
        {
            throw new FormatException($"'{value}' is not a valid absolute Smithy shape ID.");
        }

        return shapeId;
    }

    public static bool TryParse(string? value, out ShapeId shapeId)
    {
        shapeId = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hashIndex = value.IndexOf('#', StringComparison.Ordinal);
        if (hashIndex <= 0 || hashIndex == value.Length - 1)
        {
            return false;
        }

        var @namespace = value[..hashIndex];
        var shapeAndMember = value[(hashIndex + 1)..];
        var memberIndex = shapeAndMember.IndexOf('$', StringComparison.Ordinal);
        var name = memberIndex >= 0 ? shapeAndMember[..memberIndex] : shapeAndMember;
        var memberName = memberIndex >= 0 ? shapeAndMember[(memberIndex + 1)..] : null;

        if (
            !IsValidNamespace(@namespace)
            || !IsValidIdentifier(name)
            || (memberName is not null && !IsValidIdentifier(memberName))
        )
        {
            return false;
        }

        shapeId = new ShapeId(@namespace, name, memberName);
        return true;
    }

    public ShapeId WithMember(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        return IsValidIdentifier(memberName)
            ? this with
            {
                MemberName = memberName,
            }
            : throw new FormatException($"'{memberName}' is not a valid Smithy member name.");
    }

    public override string ToString()
    {
        return MemberName is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Namespace}#{Name}")
            : string.Create(CultureInfo.InvariantCulture, $"{Namespace}#{Name}${MemberName}");
    }

    private static bool IsValidNamespace(string value)
    {
        return value.Split('.').All(IsValidIdentifier);
    }

    private static bool IsValidIdentifier(string value)
    {
        if (value.Length == 0 || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        return value.Skip(1).All(IsIdentifierPart);
    }

    private static bool IsIdentifierStart(char value)
    {
        return value == '_' || char.IsAsciiLetter(value);
    }

    private static bool IsIdentifierPart(char value)
    {
        return value == '_' || char.IsAsciiLetterOrDigit(value);
    }
}
