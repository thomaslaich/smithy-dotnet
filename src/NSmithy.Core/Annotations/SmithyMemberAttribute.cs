namespace NSmithy.Core.Annotations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class SmithyMemberAttribute(string name, string target) : Attribute
{
    public string Name { get; } = name;

    public string Target { get; } = target;

    public bool IsRequired { get; init; }

    public bool IsSparse { get; init; }

    public string? JsonName { get; init; }
}
