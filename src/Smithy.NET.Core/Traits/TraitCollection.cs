using System.Collections.ObjectModel;

namespace Smithy.NET.Core.Traits;

public sealed class TraitCollection(IDictionary<ShapeId, Document> dictionary)
    : ReadOnlyDictionary<ShapeId, Document>(dictionary)
{
    public static TraitCollection None { get; } = new(new Dictionary<ShapeId, Document>());

    public bool Has(ShapeId id)
    {
        return ContainsKey(id);
    }

    public Document? GetValueOrDefault(ShapeId id)
    {
        return TryGetValue(id, out var value) ? value : null;
    }
}
