using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

/// <summary>What the CBOR codec needs to know about one member: built once, read per value.</summary>
internal sealed class CborMemberPlan
{
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");
    private static readonly ShapeId ClientOptionalTrait = new("smithy.api", "clientOptional");

    public CborMemberPlan(
        string name,
        bool isRequired,
        Schema target,
        Func<ShapeId, Trait?> getTrait,
        CborPlans plans
    )
    {
        Name = name;
        IsRequired = isRequired;
        Target = target;
        Default =
            getTrait(ClientOptionalTrait) is null
            && getTrait(DefaultTrait) is { } trait
            && trait.Value.Kind != DocumentKind.Null
                ? trait.Value
                : null;
        Shape = plans.ForTarget(target);
    }

    public string Name { get; }

    public bool IsRequired { get; }

    public Schema Target { get; }

    /// <summary>The modeled <c>@default</c>, or null when it has none or is <c>@clientOptional</c>.</summary>
    public Document? Default { get; }

    /// <summary>The plan of the member's target when it is an aggregate.</summary>
    public CborShapePlan? Shape { get; }
}

/// <summary>The CBOR codec's plan for one aggregate shape, indexed by member position.</summary>
internal sealed class CborShapePlan(ShapeKind kind, bool sparse)
{
    private Dictionary<string, int> indexes = [];

    public ShapeKind Kind { get; } = kind;

    /// <summary>Whether a list or map may hold nulls.</summary>
    public bool Sparse { get; } = sparse;

    public CborMemberPlan[] Members { get; private set; } = [];

    /// <summary>Members a projection excludes; null when every member is written and read.</summary>
    public bool[]? Excluded { get; private init; }

    public void SetMembers(CborMemberPlan[] members)
    {
        Members = members;
        indexes = [];
        for (var index = 0; index < members.Length; index++)
        {
            indexes.TryAdd(members[index].Name, index);
        }
    }

    public bool IsIncluded(int member) => Excluded is null || !Excluded[member];

    public int IndexOf(string name) => indexes.TryGetValue(name, out var index) ? index : -1;

    /// <summary>A copy that excludes every member <paramref name="include"/> rejects.</summary>
    public CborShapePlan Project(Func<string, bool> include)
    {
        var projected = new CborShapePlan(Kind, Sparse)
        {
            Excluded = [.. Members.Select(member => !include(member.Name))],
        };
        projected.SetMembers(Members);
        return projected;
    }
}

/// <summary>Builds and memoizes plans for one codec.</summary>
internal sealed class CborPlans
{
    private static readonly ShapeId SparseTrait = new("smithy.api", "sparse");

    private readonly Dictionary<Schema, CborShapePlan> plans = new(
        ReferenceEqualityComparer.Instance
    );

    /// <summary>The plan for a top-level value, which is no member of anything.</summary>
    public CborMemberPlan ForRoot(Schema schema) =>
        new(schema.Id.Name, isRequired: true, schema, schema.Resolved.GetTrait, this);

    /// <summary>The plan for a member's target, or null when the target is a simple shape.</summary>
    public CborShapePlan? ForTarget(Schema target)
    {
        var resolved = target.Resolved;
        if (resolved is INullableSchema nullable)
        {
            resolved = nullable.Target.Resolved;
        }

        if (
            resolved.Kind
                is not (
                    ShapeKind.Structure
                    or ShapeKind.Union
                    or ShapeKind.List
                    or ShapeKind.Set
                    or ShapeKind.Map
                )
            || resolved is IEventStreamSchema
        )
        {
            return null;
        }

        if (plans.TryGetValue(resolved, out var existing))
        {
            return existing;
        }

        // Registered before its members are built, so a shape that reaches itself links back to
        // this plan rather than recursing forever.
        var plan = new CborShapePlan(resolved.Kind, resolved.HasTrait(SparseTrait));
        plans.Add(resolved, plan);
        plan.SetMembers(
            resolved switch
            {
                IStructSchema structure => [.. structure.Members.Select(Member)],
                IListSchema list => [Member(list.ElementMember)],
                IMapSchema map => [Member(map.KeyMember), Member(map.ValueMember)],
                IUnionSchema union =>
                [
                    .. union.Cases.Select(@case => new CborMemberPlan(
                        @case.Name,
                        isRequired: true,
                        @case.Target,
                        id =>
                            @case.Traits.TryGetValue(id, out var trait)
                                ? trait
                                : @case.Target.Resolved.GetTrait(id),
                        this
                    )),
                ],
                _ => [],
            }
        );
        return plan;
    }

    private CborMemberPlan Member(IMemberSchema member) =>
        new(member.Name, member.IsRequired, member.Target, member.GetTrait, this);
}
