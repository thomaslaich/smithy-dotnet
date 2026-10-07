using System.Collections.Frozen;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

/// <summary>What the Proto codec needs to know about one field: built once, read per value.</summary>
internal sealed class ProtoMemberPlan
{
    public ProtoMemberPlan(
        string name,
        int fieldNumber,
        Schema target,
        IReadOnlyDictionary<ShapeId, Trait> memberTraits,
        ProtoPlans plans
    )
    {
        Name = name;
        FieldNumber = fieldNumber;
        Target = target;
        var unwrapped = ProtoWire.Unwrap(target);
        Kind = unwrapped.Kind;
        IsInlinedUnion = ProtoWire.IsInlinedUnion(unwrapped);
        Shape = plans.ForTarget(target);
        if (Kind is not (ShapeKind.List or ShapeKind.Set or ShapeKind.Map))
        {
            WireType = ProtoWire.WireTypeOf(Kind, memberTraits);
            Encoding = ProtoWire.IntEncodingOf(Kind, memberTraits);
        }

        if (Kind == ShapeKind.Enum)
        {
            EnumValues = ProtoWire.EnumValues(unwrapped);
            EnumOrdinals = ProtoWire.EnumOrdinals(unwrapped);
        }
    }

    /// <summary>The member's name in the model, used in error paths.</summary>
    public string Name { get; }

    public int FieldNumber { get; }

    public Schema Target { get; }

    public ShapeKind Kind { get; }

    public WireType WireType { get; }

    public ProtoWire.IntEncoding Encoding { get; }

    /// <summary>
    /// Whether a union member is written as its cases' fields in the enclosing message rather than
    /// as a nested message of its own.
    /// </summary>
    public bool IsInlinedUnion { get; }

    /// <summary>A string enum's values by proto ordinal minus one (0 is UNSPECIFIED).</summary>
    public string[]? EnumValues { get; }

    public FrozenDictionary<string, int>? EnumOrdinals { get; }

    /// <summary>The plan of the member's target when it is an aggregate.</summary>
    public ProtoShapePlan? Shape { get; }
}

/// <summary>The Proto codec's plan for one aggregate shape, indexed by member position.</summary>
internal sealed class ProtoShapePlan(ShapeKind kind, bool sparse)
{
    private Dictionary<int, (int Member, int Case)> fields = [];

    public ShapeKind Kind { get; } = kind;

    /// <summary>Whether a map's values are google.protobuf.Value, so they can be null.</summary>
    public bool Sparse { get; } = sparse;

    public ProtoMemberPlan[] Members { get; private set; } = [];

    /// <summary>Whether a list's elements are packed into one length-delimited field.</summary>
    public bool Packed { get; private set; }

    public void SetMembers(ProtoMemberPlan[] members)
    {
        Members = members;
        Packed =
            Kind is ShapeKind.List or ShapeKind.Set && ProtoWire.IsPackableScalar(members[0].Kind);
        fields = [];
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            if (member.IsInlinedUnion)
            {
                // An inlined union's cases are fields of this message.
                var cases = member.Shape!.Members;
                for (var @case = 0; @case < cases.Length; @case++)
                {
                    AddField(cases[@case].FieldNumber, index, @case);
                }
            }
            else
            {
                AddField(member.FieldNumber, index, -1);
            }
        }
    }

    /// <summary>The member, and for an inlined union the case, a field number belongs to.</summary>
    public bool TryGetField(int fieldNumber, out (int Member, int Case) field) =>
        fields.TryGetValue(fieldNumber, out field);

    private void AddField(int fieldNumber, int member, int @case)
    {
        if (!fields.TryAdd(fieldNumber, (member, @case)))
        {
            throw new InvalidOperationException($"Duplicate protobuf field number {fieldNumber}.");
        }
    }
}

/// <summary>Builds and memoizes plans for one codec.</summary>
internal sealed class ProtoPlans
{
    private readonly Dictionary<Schema, ProtoShapePlan> plans = new(
        ReferenceEqualityComparer.Instance
    );

    /// <summary>The plan for a top-level message, which is no field of anything.</summary>
    public ProtoMemberPlan ForRoot(Schema schema)
    {
        var kind = ProtoWire.Unwrap(schema).Kind;
        if (kind is not (ShapeKind.Structure or ShapeKind.Union))
        {
            throw new InvalidOperationException(
                "Protobuf messages must be backed by a structure or union schema."
            );
        }

        return new(schema.Id.Name, 0, schema, new Dictionary<ShapeId, Trait>(), this);
    }

    /// <summary>The plan for a field's target, or null when the target is a simple shape.</summary>
    public ProtoShapePlan? ForTarget(Schema target)
    {
        var resolved = ProtoWire.Unwrap(target);
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
        var plan = new ProtoShapePlan(resolved.Kind, ProtoWire.IsSparse(resolved));
        plans.Add(resolved, plan);
        plan.SetMembers(
            resolved switch
            {
                IStructSchema structure =>
                [
                    .. structure.Members.Select(
                        (member, index) =>
                            new ProtoMemberPlan(
                                member.Name,
                                member.Target.Resolved is var memberTarget
                                && ProtoWire.IsInlinedUnion(ProtoWire.Unwrap(memberTarget))
                                    ? 0
                                    : ProtoWire.FieldNumber(member.Id, member.MemberTraits, index),
                                member.Target,
                                member.MemberTraits,
                                this
                            )
                    ),
                ],
                IListSchema list =>
                [
                    new ProtoMemberPlan(
                        list.ElementMember.Name,
                        0,
                        list.ElementMember.Target,
                        list.ElementMember.MemberTraits,
                        this
                    ),
                ],
                IMapSchema map =>
                [
                    new ProtoMemberPlan(
                        map.KeyMember.Name,
                        1,
                        map.KeyMember.Target,
                        map.KeyMember.MemberTraits,
                        this
                    ),
                    new ProtoMemberPlan(
                        map.ValueMember.Name,
                        2,
                        map.ValueMember.Target,
                        map.ValueMember.MemberTraits,
                        this
                    ),
                ],
                IUnionSchema union =>
                [
                    .. union.Cases.Select(
                        (@case, index) =>
                            new ProtoMemberPlan(
                                @case.Name,
                                ProtoWire.FieldNumber(@case.Id, @case.MemberTraits, index),
                                @case.Target,
                                @case.MemberTraits,
                                this
                            )
                    ),
                ],
                _ => [],
            }
        );
        return plan;
    }
}
