using Smithy.NET.Core.Traits;

namespace Smithy.NET.Core.Shapes;

public sealed record ShapeMetadata(
    ShapeId Id,
    ShapeKind Kind,
    TraitCollection Traits,
    IReadOnlyList<MemberMetadata> Members
);
