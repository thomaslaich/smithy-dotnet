using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

internal static class JsonWire
{
    private static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");
    private static readonly ShapeId JsonNameTrait = new("smithy.api", "jsonName");
    private static readonly ShapeId SparseTrait = new("smithy.api", "sparse");
    private static readonly ShapeId AlloyDiscriminatedTrait = new("alloy", "discriminated");
    private static readonly ShapeId AlloyJsonUnknownTrait = new("alloy", "jsonUnknown");

    // The JSON property name for a member or union case: @jsonName if present, else the name.
    internal static string WireName(IReadOnlyDictionary<ShapeId, Trait> traits, string fallback) =>
        traits.TryGetValue(JsonNameTrait, out var trait) ? trait.Value.AsString() : fallback;

    internal static bool IsOpenUnion(IUnionSchema schema) =>
        ((Schema)schema).Traits.ContainsKey(AlloyDiscriminatedTrait)
        || GetJsonUnknownCase(schema) is not null;

    internal static bool IsSparse(Schema schema) => schema.HasTrait(SparseTrait);

    internal static bool TryCreateDefaultValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        out T? value
    )
    {
        if (
            traits.ContainsKey(ClientOptionalTrait)
            || !traits.TryGetValue(DefaultTrait, out var trait)
            || trait.Value.Kind == DocumentKind.Null
        )
        {
            value = default;
            return false;
        }

        value = CreateDefaultValue(schema, trait.Value);
        return value is not null;
    }

    private static T? CreateDefaultValue<T>(Schema<T> schema, Document value)
    {
        var resolved = schema.Resolved;
        if (resolved is INullableSchema nullable)
        {
            return (T?)CreateDefaultValue((dynamic)nullable.Target, value);
        }

        return resolved.Kind switch
        {
            ShapeKind.Boolean => (T)(object)value.AsBoolean(),
            ShapeKind.Byte => (T)(object)(sbyte)value.AsNumber(),
            ShapeKind.Short => (T)(object)(short)value.AsNumber(),
            ShapeKind.Integer => (T)(object)(int)value.AsNumber(),
            ShapeKind.Long => (T)(object)(long)value.AsNumber(),
            ShapeKind.Float => (T)(object)(float)value.AsNumber(),
            ShapeKind.Double => (T)(object)(double)value.AsNumber(),
            ShapeKind.BigInteger => (T)(object)new BigInteger(value.AsNumber()),
            ShapeKind.BigDecimal => (T)(object)value.AsNumber(),
            ShapeKind.String => (T)(object)value.AsString(),
            ShapeKind.Enum => (T)((IStringEnumSchema)resolved).CreateObject(value.AsString()),
            ShapeKind.IntEnum => (T)((IIntEnumSchema)resolved).CreateObject((int)value.AsNumber()),
            ShapeKind.Blob => (T)(object)Convert.FromBase64String(value.AsString()),
            ShapeKind.Timestamp => (T)
                (object)DateTimeOffset.FromUnixTimeSeconds((long)value.AsNumber()),
            ShapeKind.Document => (T)(object)value,
            ShapeKind.List or ShapeKind.Set when resolved is IListSchema list => CreateDefaultList(
                (dynamic)list,
                value
            ),
            ShapeKind.Map when resolved is IMapSchema map => CreateDefaultMap((dynamic)map, value),
            _ => null,
        };
    }

    private static TCollection CreateDefaultList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema,
        Document value
    )
    {
        var builder = schema.CreateTypedBuilder();
        foreach (var item in value.AsArray())
        {
            schema.Add(builder, CreateDefaultValue(schema.TypedElementMember.TargetSchema, item)!);
        }

        return schema.Build(builder);
    }

    private static TDictionary CreateDefaultMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema,
        Document value
    )
    {
        var builder = schema.CreateTypedBuilder();
        foreach (var entry in value.AsObject())
        {
            schema.Add(
                builder,
                entry.Key,
                CreateDefaultValue(schema.TypedValueMember.TargetSchema, entry.Value)!
            );
        }

        return schema.Build(builder);
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
