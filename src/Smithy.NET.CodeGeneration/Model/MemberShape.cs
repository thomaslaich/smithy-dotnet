using Smithy.NET.Core;
using Smithy.NET.Core.Traits;

namespace Smithy.NET.CodeGeneration.Model;

public sealed record MemberShape(
    ShapeId Id,
    string Name,
    ShapeId Target,
    TraitCollection Traits,
    Document? DefaultValue
)
{
    public bool IsRequired => Traits.Has(SmithyPrelude.RequiredTrait);

    public bool IsClientOptional => Traits.Has(SmithyPrelude.ClientOptionalTrait);
}
