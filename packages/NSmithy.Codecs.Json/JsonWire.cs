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

    private static MemberSchema? GetJsonUnknownCase(IUnionSchema schema) =>
        schema.Cases.FirstOrDefault(IsJsonUnknownCase);

    internal static bool IsJsonUnknownCase(MemberSchema @case) =>
        @case.MemberTraits.ContainsKey(AlloyJsonUnknownTrait);

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
}
