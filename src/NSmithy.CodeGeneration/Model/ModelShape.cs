using NSmithy.Core;
using NSmithy.Core.Traits;

namespace NSmithy.CodeGeneration.Model;

public sealed record ModelShape(
    ShapeId Id,
    ShapeKind Kind,
    TraitCollection Traits,
    IReadOnlyDictionary<string, MemberShape> Members,
    ShapeId? Target,
    ShapeId? Input,
    ShapeId? Output,
    IReadOnlyList<ShapeId> Errors,
    IReadOnlyList<ShapeId> Operations,
    IReadOnlyList<ShapeId> Resources
)
{
    public IReadOnlyList<ShapeId> Protocols { get; } =
    [
        .. Traits
            .Keys.Where(id =>
                !string.Equals(id.Namespace, SmithyPrelude.Namespace, StringComparison.Ordinal)
            )
            .OrderBy(id => id.ToString(), StringComparer.Ordinal),
    ];
}
