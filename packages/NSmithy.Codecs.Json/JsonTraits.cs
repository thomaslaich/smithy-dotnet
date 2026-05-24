namespace NSmithy.Codecs.Json;

/// <summary>Well-known trait ids that the JSON codec consults on member schemas.</summary>
internal static class JsonTraits
{
    /// <summary>The <c>smithy.api#jsonName</c> trait — overrides the JSON property name for a struct member.</summary>
    public static readonly NSmithy.Core.ShapeId JsonName = new("smithy.api", "jsonName");

    /// <summary>The <c>smithy.api#timestampFormat</c> trait — controls timestamp encoding.</summary>
    public static readonly NSmithy.Core.ShapeId TimestampFormat = new(
        "smithy.api",
        "timestampFormat"
    );

    public static readonly NSmithy.Core.ShapeId Discriminated = new("alloy", "discriminated");

    public static readonly NSmithy.Core.ShapeId JsonUnknown = new("alloy", "jsonUnknown");
}
