using NSmithy.Core;

namespace NSmithy.Codecs.Json;

internal static class JsonNameResolver
{
    /// <summary>
    /// Resolve the JSON property name for a struct member: <c>smithy.api#jsonName</c> trait
    /// value if present, otherwise the schema's member name.
    /// </summary>
    public static string Resolve(Schema memberSchema)
    {
        var trait = memberSchema.GetTrait(JsonTraits.JsonName);
        if (trait is { } t && t.Value.Kind == DocumentKind.String)
        {
            return t.Value.AsString();
        }

        return memberSchema.MemberName
            ?? throw new InvalidOperationException(
                $"Schema '{memberSchema.Id}' is not a member schema and has no JSON name."
            );
    }
}
