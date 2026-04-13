using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Smithy.NET.Core;

public readonly record struct Document
{
    private readonly object? value;

    private Document(DocumentKind kind, object? value)
    {
        Kind = kind;
        this.value = value;
    }

    public DocumentKind Kind { get; }

    public static Document Null { get; } = new(DocumentKind.Null, null);

    public static Document From(bool value)
    {
        return new Document(DocumentKind.Boolean, value);
    }

    public static Document From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Document(DocumentKind.String, value);
    }

    public static Document From(decimal value)
    {
        return new Document(DocumentKind.Number, value);
    }

    public static Document From(IEnumerable<Document> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Document(DocumentKind.Array, Array.AsReadOnly(value.ToArray()));
    }

    public static Document From(IReadOnlyDictionary<string, Document> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sorted = new SortedDictionary<string, Document>(StringComparer.Ordinal);
        foreach (var item in value)
        {
            sorted.Add(item.Key, item.Value);
        }

        return new Document(
            DocumentKind.Object,
            new ReadOnlyDictionary<string, Document>(
                new Dictionary<string, Document>(sorted, StringComparer.Ordinal)
            )
        );
    }

    public static Document FromJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => Null,
            JsonValueKind.True => From(true),
            JsonValueKind.False => From(false),
            JsonValueKind.String => From(element.GetString() ?? string.Empty),
            JsonValueKind.Number => From(ReadNumber(element)),
            JsonValueKind.Array => From(element.EnumerateArray().Select(FromJsonElement)),
            JsonValueKind.Object => From(
                element
                    .EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => FromJsonElement(property.Value),
                        StringComparer.Ordinal
                    )
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported JSON value kind '{element.ValueKind}'."
            ),
        };
    }

    public bool AsBoolean()
    {
        return Kind == DocumentKind.Boolean
            ? (bool)value!
            : throw CreateKindException(DocumentKind.Boolean);
    }

    public string AsString()
    {
        return Kind == DocumentKind.String
            ? (string)value!
            : throw CreateKindException(DocumentKind.String);
    }

    public decimal AsNumber()
    {
        return Kind == DocumentKind.Number
            ? (decimal)value!
            : throw CreateKindException(DocumentKind.Number);
    }

    public IReadOnlyList<Document> AsArray()
    {
        return Kind == DocumentKind.Array
            ? (IReadOnlyList<Document>)value!
            : throw CreateKindException(DocumentKind.Array);
    }

    public IReadOnlyDictionary<string, Document> AsObject()
    {
        return Kind == DocumentKind.Object
            ? (IReadOnlyDictionary<string, Document>)value!
            : throw CreateKindException(DocumentKind.Object);
    }

    public override string ToString()
    {
        return Kind switch
        {
            DocumentKind.Null => "null",
            DocumentKind.Boolean => AsBoolean()
                .ToString(CultureInfo.InvariantCulture)
                .ToLowerInvariant(),
            DocumentKind.String => AsString(),
            DocumentKind.Number => AsNumber().ToString(CultureInfo.InvariantCulture),
            DocumentKind.Array => $"[{AsArray().Count} item(s)]",
            DocumentKind.Object => $"{{{AsObject().Count} member(s)}}",
            _ => throw new InvalidOperationException($"Unsupported document kind '{Kind}'."),
        };
    }

    private static decimal ReadNumber(JsonElement element)
    {
        return element.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : decimal.Parse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private InvalidOperationException CreateKindException(DocumentKind expectedKind)
    {
        return new InvalidOperationException(
            $"Expected a {expectedKind} document but found {Kind}."
        );
    }
}
