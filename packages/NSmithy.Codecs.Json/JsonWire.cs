using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

internal static class JsonWire
{
    private static Schema UnwrapNullable(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }

    private static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");
    private static readonly ShapeId JsonNameTrait = new("smithy.api", "jsonName");
    private static readonly ShapeId AlloyDiscriminatedTrait = new("alloy", "discriminated");
    private static readonly ShapeId AlloyJsonUnknownTrait = new("alloy", "jsonUnknown");

    // The JSON property name for a member or union case: @jsonName if present, else the name.
    internal static string WireName(IReadOnlyDictionary<ShapeId, Trait> traits, string fallback) =>
        traits.TryGetValue(JsonNameTrait, out var trait) ? trait.Value.AsString() : fallback;

    internal static bool IsOpenUnion(IUnionSchema schema) =>
        ((Schema)schema).Traits.ContainsKey(AlloyDiscriminatedTrait)
        || GetJsonUnknownCase(schema) is not null;

    internal static bool TryCreateDefaultValue(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        out object? value
    )
    {
        if (
            traits.ContainsKey(ClientOptionalTrait)
            || !traits.TryGetValue(DefaultTrait, out var trait)
            || trait.Value.Kind == DocumentKind.Null
        )
        {
            value = null;
            return false;
        }

        value = CreateDefaultValue(UnwrapNullable(schema), trait.Value);
        return value is not null;
    }

    private static object? CreateDefaultValue(Schema schema, Document value)
    {
        return schema.Kind switch
        {
            ShapeKind.Boolean => value.AsBoolean(),
            ShapeKind.Byte => (sbyte)value.AsNumber(),
            ShapeKind.Short => (short)value.AsNumber(),
            ShapeKind.Integer => (int)value.AsNumber(),
            ShapeKind.Long => (long)value.AsNumber(),
            ShapeKind.Float => (float)value.AsNumber(),
            ShapeKind.Double => (double)value.AsNumber(),
            ShapeKind.BigInteger => new BigInteger(value.AsNumber()),
            ShapeKind.BigDecimal => value.AsNumber(),
            ShapeKind.String => value.AsString(),
            ShapeKind.Enum => ((IStringEnumSchema)schema).CreateObject(value.AsString()),
            ShapeKind.IntEnum => ((IIntEnumSchema)schema).CreateObject((int)value.AsNumber()),
            ShapeKind.Blob => Convert.FromBase64String(value.AsString()),
            ShapeKind.Timestamp => DateTimeOffset.FromUnixTimeSeconds((long)value.AsNumber()),
            ShapeKind.Document => value,
            ShapeKind.List or ShapeKind.Set when schema.Resolved is IListSchema list =>
                CreateDefaultList(list, value),
            ShapeKind.Map when schema.Resolved is IMapSchema map => CreateDefaultMap(map, value),
            _ => null,
        };
    }

    private static object CreateDefaultList(IListSchema schema, Document value)
    {
        var builder = schema.CreateBuilder();
        foreach (var item in value.AsArray())
        {
            schema.AddObject(builder, CreateDefaultValue(UnwrapNullable(schema.Element), item));
        }

        return schema.BuildObject(builder);
    }

    private static object CreateDefaultMap(IMapSchema schema, Document value)
    {
        var builder = schema.CreateBuilder();
        foreach (var entry in value.AsObject())
        {
            schema.AddObject(
                builder,
                entry.Key,
                CreateDefaultValue(UnwrapNullable(schema.Value), entry.Value)
            );
        }

        return schema.BuildObject(builder);
    }

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

    internal static float ReadFloat(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "NaN" => float.NaN,
                "Infinity" => float.PositiveInfinity,
                "-Infinity" => float.NegativeInfinity,
                var s => float.Parse(s!, CultureInfo.InvariantCulture),
            }
            : value.GetSingle();

    internal static double ReadDouble(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                var s => double.Parse(s!, CultureInfo.InvariantCulture),
            }
            : value.GetDouble();
}
