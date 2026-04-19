using SmithyNet.Core;
using SmithyNet.Core.Traits;

namespace SmithyNet.CodeGeneration.Model;

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
