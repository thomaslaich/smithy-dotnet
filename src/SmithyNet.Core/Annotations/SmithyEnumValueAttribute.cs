namespace SmithyNet.Core.Annotations;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SmithyEnumValueAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}
