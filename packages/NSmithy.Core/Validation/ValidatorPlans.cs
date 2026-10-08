using NSmithy.Core.Serde;

namespace NSmithy.Core.Validation;

/// <summary>What the validator checks on one edge: a member, list element, map key or value, or union case.</summary>
internal sealed class ValidatorMemberPlan
{
    public required string Name { get; init; }

    public required bool IsRequired { get; init; }

    /// <summary>The shape a missing required member is reported against: the member's target.</summary>
    public required ShapeId TargetId { get; init; }

    /// <summary>Constraints the edge itself declares, reported against the member.</summary>
    public EdgeConstraints? Member { get; init; }

    /// <summary>Constraints the target shape declares, reported against the shape.</summary>
    public EdgeConstraints? Target { get; init; }

    public IStringEnumSchema? StringEnum { get; init; }

    public IIntEnumSchema? IntEnum { get; init; }

    public bool UniqueItems { get; init; }

    /// <summary>The plan of the target when it is an aggregate.</summary>
    public ValidatorShapePlan? Shape { get; set; }

    /// <summary>Whether anything on or below this edge can fail validation.</summary>
    public bool Needs { get; set; }
}

/// <summary>The validator's plan for one aggregate shape, indexed by member position.</summary>
internal sealed class ValidatorShapePlan(ShapeKind kind)
{
    public ShapeKind Kind { get; } = kind;

    public ValidatorMemberPlan[] Members { get; set; } = [];

    /// <summary>Whether anything in this shape can fail validation.</summary>
    public bool Needs { get; set; }
}

/// <summary>Builds and memoizes validator plans.</summary>
internal sealed class ValidatorPlans
{
    private readonly Dictionary<Schema, ValidatorShapePlan> plans = new(
        ReferenceEqualityComparer.Instance
    );

    public ValidatorMemberPlan ForRoot(Schema schema) =>
        Edge(schema.Id.Name, isRequired: false, schema, memberConstraints: null);

    private ValidatorMemberPlan Edge(
        string name,
        bool isRequired,
        Schema target,
        EdgeConstraints? memberConstraints
    )
    {
        var resolved = Unwrap(target);
        // An enum that declares no values (an open set) constrains nothing.
        var stringEnum = resolved is IStringEnumSchema s && s.Values.Count > 0 ? s : null;
        var intEnum = resolved is IIntEnumSchema i && i.Values.Count > 0 ? i : null;
        var plan = new ValidatorMemberPlan
        {
            Name = name,
            IsRequired = isRequired,
            TargetId = target.Id,
            Member = memberConstraints,
            Target = EdgeConstraints.From(resolved.Traits, resolved.Id, resolved),
            StringEnum = stringEnum,
            IntEnum = intEnum,
            UniqueItems = resolved.HasTrait(ConstraintTraits.UniqueItems),
        };
        plan.Shape = ForTarget(resolved);
        plan.Needs =
            isRequired
            || plan.Member is not null
            || plan.Target is not null
            || plan.StringEnum is not null
            || plan.IntEnum is not null
            || plan.UniqueItems
            || plan.Shape?.Needs == true;
        return plan;
    }

    private ValidatorShapePlan? ForTarget(Schema resolved)
    {
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
            // A shape reached again through recursion is validated by the plan already being built;
            // whether it needs anything is settled by that plan's own members.
            return existing;
        }

        var plan = new ValidatorShapePlan(resolved.Kind);
        plans.Add(resolved, plan);
        plan.Members = resolved switch
        {
            IStructSchema structure =>
            [
                .. structure.Members.Select(member =>
                    Edge(
                        member.Name,
                        member.IsRequired,
                        member.Target,
                        EdgeConstraints.From(member.MemberTraits, member.Id, member.Target)
                    )
                ),
            ],
            IListSchema list =>
            [
                Edge(
                    list.ElementMember.Name,
                    isRequired: false,
                    list.ElementMember.Target,
                    EdgeConstraints.From(
                        list.ElementMember.MemberTraits,
                        list.ElementMember.Id,
                        list.ElementMember.Target
                    )
                ),
            ],
            IMapSchema map => [MapKey(map.KeyMember), MapValue(map.ValueMember)],
            IUnionSchema union =>
            [
                .. union.Cases.Select(@case =>
                    Edge(
                        @case.Name,
                        isRequired: false,
                        @case.Target,
                        EdgeConstraints.From(@case.MemberTraits, @case.Id, @case.Target)
                    )
                ),
            ],
            _ => [],
        };
        plan.Needs = plan.Members.Any(member => member.Needs);
        return plan;
    }

    /// <summary>
    /// A map key is a string whatever it targets: its own traits constrain the string, while an
    /// enum target closes the set of strings allowed.
    /// </summary>
    private static ValidatorMemberPlan MapKey(MemberSchema key)
    {
        var target = Unwrap(key.Target);
        var plan = new ValidatorMemberPlan
        {
            Name = key.Name,
            IsRequired = false,
            TargetId = key.Target.Id,
            Member = EdgeConstraints.From(key.MemberTraits, key.Id, Schemas.String),
            StringEnum = target is IStringEnumSchema @enum && @enum.Values.Count > 0 ? @enum : null,
        };
        plan.Needs = plan.Member is not null || plan.StringEnum is not null;
        return plan;
    }

    private ValidatorMemberPlan MapValue(MemberSchema value) =>
        Edge(
            value.Name,
            isRequired: false,
            value.Target,
            EdgeConstraints.From(value.MemberTraits, value.Id, value.Target)
        );

    private static Schema Unwrap(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }
}
