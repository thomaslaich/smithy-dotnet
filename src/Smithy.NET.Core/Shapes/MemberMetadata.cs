using Smithy.NET.Core.Traits;

namespace Smithy.NET.Core.Shapes;

public sealed record MemberMetadata(
    ShapeId Id,
    string Name,
    ShapeId Target,
    TraitCollection Traits,
    Document? DefaultValue
);
