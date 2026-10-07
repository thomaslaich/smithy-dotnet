using System.Numerics;
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

/// <summary>
/// Checks each value as the schema writes it. A nested aggregate gets its own serializer
/// positioned on that aggregate's plan and path; an aggregate whose plan needs nothing is not
/// descended into at all.
/// </summary>
internal struct ValidatingSerializer : IShapeSerializer
{
    // decimal cannot hold the whole double, float or BigInteger range. Magnitudes beyond it are
    // clamped rather than converted, so an out-of-range value compares as out of range instead of
    // throwing on the request path; any bound a model can express is far inside these limits.
    private const double DecimalUpperBound = 7.9e28;
    private const double DecimalLowerBound = -7.9e28;

    private readonly ValidatorShapePlan? container;
    private readonly ValidatorMemberPlan? root;
    private readonly PathNode? node;
    private readonly List<SmithyValidationError> errors;

    // The next list element's index, and the key of the map entry whose value comes next.
    private int index;
    private string? key;

    public ValidatingSerializer(ValidatorMemberPlan root, List<SmithyValidationError> errors)
    {
        this.root = root;
        this.errors = errors;
    }

    private ValidatingSerializer(
        ValidatorShapePlan container,
        PathNode node,
        List<SmithyValidationError> errors
    )
    {
        this.container = container;
        this.node = node;
        this.errors = errors;
    }

    private readonly ValidatorMemberPlan Entry(int member) =>
        container is null ? root!
        : container.Kind is ShapeKind.List or ShapeKind.Set ? container.Members[0]
        : container.Members[member];

    /// <summary>The path of the value <paramref name="member"/> is written as, advancing a list's index.</summary>
    private ValuePath PathOf(int member) =>
        container?.Kind switch
        {
            null => new ValuePath(node),
            ShapeKind.List or ShapeKind.Set => new ValuePath(node, index: index++),
            ShapeKind.Map => new ValuePath(node, key!),
            _ => new ValuePath(node, container.Members[member].Name),
        };

    public readonly bool WritesDefault(int member) => false;

    public void WriteNull(int member)
    {
        if (container?.Kind == ShapeKind.Structure)
        {
            var entry = container.Members[member];
            if (entry.IsRequired)
            {
                var memberPath = PathOf(member).Render();
                errors.Add(
                    new SmithyValidationError(
                        memberPath,
                        entry.TargetId,
                        ConstraintTraits.Required,
                        ConstraintMessages.Failed(memberPath, "Member must not be null")
                    )
                );
            }

            return;
        }

        // A null element still has a position in its list.
        if (container?.Kind is ShapeKind.List or ShapeKind.Set)
        {
            index++;
        }
    }

    public void WriteBoolean(int member, bool value) => Skip(member);

    public void WriteByte(int member, sbyte value) => WriteNumber(member, value);

    public void WriteShort(int member, short value) => WriteNumber(member, value);

    public void WriteInteger(int member, int value) => WriteNumber(member, value);

    public void WriteLong(int member, long value) => WriteNumber(member, value);

    public void WriteFloat(int member, float value) => WriteNumber(member, FromDouble(value));

    public void WriteDouble(int member, double value) => WriteNumber(member, FromDouble(value));

    public void WriteBigInteger(int member, BigInteger value) =>
        WriteNumber(
            member,
            value >= new BigInteger(decimal.MinValue)
                ? value <= new BigInteger(decimal.MaxValue)
                    ? (decimal)value
                    : decimal.MaxValue
                : decimal.MinValue
        );

    public void WriteBigDecimal(int member, decimal value) => WriteNumber(member, value);

    private void WriteNumber(int member, decimal? value)
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        entry.Member?.CheckRange(value, valuePath, errors);
        entry.Target?.CheckRange(value, valuePath, errors);
    }

    public void WriteString(int member, string value)
    {
        // A map's key is checked at the map itself: the key is not a value sitting at the entry's
        // pointer, the entry's value is.
        if (container?.Kind == ShapeKind.Map && member == 0)
        {
            var keyPlan = container.Members[0];
            keyPlan.Member?.CheckString(value, new ValuePath(node), errors);
            if (keyPlan.StringEnum is { } keyEnum && !keyEnum.Contains(value))
            {
                errors.Add(
                    ConstraintMessages.EnumMembership(
                        new ValuePath(node).Render(),
                        keyPlan.TargetId,
                        keyEnum.PublishedValues
                    )
                );
            }

            key = value;
            return;
        }

        var entry = Entry(member);
        var valuePath = PathOf(member);
        entry.Member?.CheckString(value, valuePath, errors);
        entry.Target?.CheckString(value, valuePath, errors);
    }

    public void WriteBlob(int member, byte[] value)
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        entry.Member?.CheckLength(value.Length, valuePath, errors);
        entry.Target?.CheckLength(value.Length, valuePath, errors);
    }

    public void WriteTimestamp(int member, DateTimeOffset value) => Skip(member);

    public void WriteDocument(int member, Document value) => Skip(member);

    // Generated enum types stay open, so an unrecognized value deserializes; the server is where
    // that openness stops.
    public void WriteStringEnum(int member, string value)
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        if (entry.StringEnum is { } @enum && !@enum.Contains(value))
        {
            errors.Add(
                ConstraintMessages.EnumMembership(
                    valuePath.Render(),
                    entry.TargetId,
                    @enum.PublishedValues
                )
            );
        }
    }

    public void WriteIntEnum(int member, int value)
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        if (entry.IntEnum is { } @enum && !@enum.Contains(value))
        {
            errors.Add(
                ConstraintMessages.EnumMembership(
                    valuePath.Render(),
                    entry.TargetId,
                    @enum.Values.Select(number =>
                        number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    )
                )
            );
        }
    }

    public void WriteStream(int member, Stream value) => Skip(member);

    public void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    ) => Skip(member);

    public void WriteStruct<T>(int member, T value, IStructSchema<T> schema)
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        if (entry.Shape is { Needs: true } plan)
        {
            var nested = new ValidatingSerializer(plan, valuePath.ToNode(), errors);
            schema.SerializeMembers(value, ref nested);
        }
    }

    public void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        if (entry.Member is not null || entry.Target is not null)
        {
            var count = Count(schema.GetElements(value));
            entry.Member?.CheckLength(count, valuePath, errors);
            entry.Target?.CheckLength(count, valuePath, errors);
        }

        if (entry.UniqueItems)
        {
            var elements = new UniqueElements();
            schema.SerializeElements(value, ref elements);
            if (elements.HasDuplicate)
            {
                var rendered = valuePath.Render();
                errors.Add(
                    new SmithyValidationError(
                        rendered,
                        entry.TargetId,
                        ConstraintTraits.UniqueItems,
                        ConstraintMessages.Failed(rendered, "Member must have unique values")
                    )
                );
            }
        }

        if (entry.Shape is { Needs: true } plan)
        {
            var nested = new ValidatingSerializer(plan, valuePath.ToNode(), errors);
            schema.SerializeElements(value, ref nested);
        }
    }

    public void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        if (entry.Member is not null || entry.Target is not null)
        {
            var count = Count(schema.GetEntries(value));
            entry.Member?.CheckLength(count, valuePath, errors);
            entry.Target?.CheckLength(count, valuePath, errors);
        }

        if (entry.Shape is { Needs: true } plan)
        {
            var nested = new ValidatingSerializer(plan, valuePath.ToNode(), errors);
            schema.SerializeEntries(value, ref nested);
        }
    }

    public void WriteUnion<T>(int member, T value, IUnionSchema<T> schema)
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        if (entry.Shape is { Needs: true } plan)
        {
            var nested = new ValidatingSerializer(plan, valuePath.ToNode(), errors);
            schema.SerializeCase(value, ref nested);
        }
    }

    // A value with nothing to check still occupies its position in a list.
    private void Skip(int member)
    {
        if (container?.Kind is ShapeKind.List or ShapeKind.Set)
        {
            index++;
        }
    }

    private static int Count<TItem>(IEnumerable<TItem> items) =>
        items is ICollection<TItem> collection ? collection.Count : items.Count();

    private static decimal? FromDouble(double number) =>
        !double.IsFinite(number) ? null
        : number >= DecimalUpperBound ? decimal.MaxValue
        : number <= DecimalLowerBound ? decimal.MinValue
        : (decimal)number;
}
