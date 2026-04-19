using SmithyNet.Core;

namespace SmithyNet.CodeGeneration.Model;

public sealed record SmithyModel(
    string SmithyVersion,
    IReadOnlyDictionary<string, Document> Metadata,
    IReadOnlyDictionary<ShapeId, ModelShape> Shapes
)
{
    public ModelShape GetShape(ShapeId id)
    {
        return Shapes.TryGetValue(id, out var shape)
            ? shape
            : throw new KeyNotFoundException($"Shape '{id}' was not found in the Smithy model.");
    }
}
