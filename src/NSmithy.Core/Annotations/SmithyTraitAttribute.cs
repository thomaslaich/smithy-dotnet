namespace NSmithy.Core.Annotations;

[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Property
        | AttributeTargets.Field,
    AllowMultiple = true
)]
public sealed class SmithyTraitAttribute(string id) : Attribute
{
    public string Id { get; } = id;

    public string? Value { get; init; }
}
