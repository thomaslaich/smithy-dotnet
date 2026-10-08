using System.Numerics;
using NSmithy.Core.Serde;

namespace NSmithy.Core.Validation;

/// <summary>
/// Checks each value as the schema writes it. A nested aggregate gets its own serializer
/// positioned on that aggregate's plan and path; an aggregate whose plan needs nothing is not
/// descended into at all.
/// </summary>
internal struct ShapeValidator : IShapeSerializer
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

    public ShapeValidator(ValidatorMemberPlan root, List<SmithyValidationError> errors)
    {
        this.root = root;
        this.errors = errors;
    }

    private ShapeValidator(
        ValidatorShapePlan container,
        PathNode node,
        List<SmithyValidationError> errors
    )
    {
        this.container = container;
        this.node = node;
        this.errors = errors;
    }

    // A list's elements are its member 0, so they index like any other member.
    private readonly ValidatorMemberPlan Entry(int member) =>
        member == MemberIndex.Root ? root! : container!.Members[member];

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
            var nested = new ShapeValidator(plan, valuePath.ToNode(), errors);
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
            var nested = new ShapeValidator(plan, valuePath.ToNode(), errors);
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
            var nested = new ShapeValidator(plan, valuePath.ToNode(), errors);
            schema.SerializeEntries(value, ref nested);
        }
    }

    public void WriteUnion<T>(int member, T value, UnionSchema<T> schema)
    {
        var entry = Entry(member);
        var valuePath = PathOf(member);
        if (entry.Shape is { Needs: true } plan)
        {
            var nested = new ShapeValidator(plan, valuePath.ToNode(), errors);
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
