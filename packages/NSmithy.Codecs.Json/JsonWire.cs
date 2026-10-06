using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

internal static class JsonWire
{
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");
    private static readonly ShapeId JsonNameTrait = new("smithy.api", "jsonName");
    private static readonly ShapeId SparseTrait = new("smithy.api", "sparse");
    private static readonly ShapeId AlloyDiscriminatedTrait = new("alloy", "discriminated");
    private static readonly ShapeId AlloyJsonUnknownTrait = new("alloy", "jsonUnknown");

    // The JSON property name for a member or union case: @jsonName if present, else the name.
    internal static string WireName(
        IReadOnlyDictionary<ShapeId, Trait> traits,
        string fallback,
        bool honorJsonNameTrait
    ) =>
        honorJsonNameTrait && traits.TryGetValue(JsonNameTrait, out var trait)
            ? trait.Value.AsString()
            : fallback;

    internal static bool IsOpenUnion(IUnionSchema schema) =>
        ((Schema)schema).Traits.ContainsKey(AlloyDiscriminatedTrait)
        || GetJsonUnknownCase(schema) is not null;

    internal static bool IsSparse(Schema schema) => schema.HasTrait(SparseTrait);

    internal static bool TryGetDiscriminatorName(IUnionSchema schema, out string discriminatorName)
    {
        if (((Schema)schema).Traits.TryGetValue(AlloyDiscriminatedTrait, out var trait))
        {
            discriminatorName = trait.Value.AsString();
            return true;
        }

        discriminatorName = string.Empty;
        return false;
    }

    private static IUnionCaseSchema? GetJsonUnknownCase(IUnionSchema schema) =>
        schema.Cases.FirstOrDefault(IsJsonUnknownCase);

    internal static bool IsJsonUnknownCase(IUnionCaseSchema @case) =>
        @case.Traits.ContainsKey(AlloyJsonUnknownTrait);

    internal static void WriteFloat(Utf8JsonWriter writer, float value)
    {
        if (float.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (float.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (float.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }

    internal static void WriteDouble(Utf8JsonWriter writer, double value)
    {
        if (double.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (double.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (double.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Runs one of <see cref="JsonElement"/>'s accessors and reports what it rejects as a malformed
    /// request. The accessors say "these bytes are not that type" by throwing — a wrong value kind,
    /// a number outside the target's range, text that is not base64 — and every one of those is the
    /// payload's fault, not the server's, so they all become the same fault the runtime answers with
    /// a structured 400.
    /// </summary>
    internal static T ReadValue<T>(JsonElement value, string expected, Func<JsonElement, T> read)
    {
        try
        {
            return read(value);
        }
        catch (Exception exception)
            when (exception
                    is FormatException
                        or InvalidOperationException
                        or OverflowException
                        or ArgumentException
            )
        {
            throw Malformed(value, expected);
        }
    }

    internal static MalformedRequestException Malformed(JsonElement value, string expected)
    {
        // The offending value is echoed back so a caller can see which one it was — truncated,
        // because the payload is untrusted and a message is not a place to copy an arbitrary
        // amount of it, and summarized for the two kinds whose raw text says nothing useful.
        const int limit = 64;
        var found = value.ValueKind switch
        {
            JsonValueKind.Object => "an object",
            JsonValueKind.Array => "an array",
            _ => value.GetRawText(),
        };
        if (found.Length > limit)
        {
            found = string.Concat(found.AsSpan(0, limit), "…");
        }

        return MalformedRequestException.Serialization($"Expected {expected} but found {found}.");
    }

    // A JSON string is a float only for the three values JSON cannot represent as a number. Parsing
    // any other string would coerce "123" into a number the caller never sent as one.
    internal static float ReadFloat(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "NaN" => float.NaN,
                "Infinity" => float.PositiveInfinity,
                "-Infinity" => float.NegativeInfinity,
                var s => throw new FormatException($"'{s}' is not a float."),
            }
            : value.GetSingle();

    internal static double ReadDouble(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                var s => throw new FormatException($"'{s}' is not a double."),
            }
            : value.GetDouble();
}
