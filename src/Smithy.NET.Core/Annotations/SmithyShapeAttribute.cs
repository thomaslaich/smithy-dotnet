namespace Smithy.NET.Core.Annotations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
public sealed class SmithyShapeAttribute(string id, ShapeKind kind) : Attribute
{
    public string Id { get; } = id;

    public ShapeKind Kind { get; } = kind;
}
