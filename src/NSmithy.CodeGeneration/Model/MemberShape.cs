using NSmithy.Core;
using NSmithy.Core.Traits;

namespace NSmithy.CodeGeneration.Model;

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
