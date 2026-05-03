using System.Collections.Frozen;

namespace NSmithy.Core;

/// <summary>
/// Runtime description of a Smithy shape. Carries the shape's id, kind, applied traits, and
/// (for aggregate shapes) the schemas of its members. A single immutable instance is
/// constructed per shape — typically as a <c>static readonly</c> field on the generated type —
/// and shared across all serialize/deserialize calls. Codecs consult the schema (and member
/// schemas) to walk traits like <c>xmlName</c>, <c>httpHeader</c>, <c>timestampFormat</c>, etc.
/// without reflecting on the runtime type.
/// </summary>
public sealed class Schema
{
    private readonly FrozenDictionary<ShapeId, Trait> traits;
    private readonly FrozenDictionary<string, Schema> membersByName;
    private readonly Lazy<Schema>? lazyTarget;
    private Schema? target;

    private Schema(
        ShapeId id,
        ShapeKind kind,
        FrozenDictionary<ShapeId, Trait> traits,
        IReadOnlyList<Schema> members,
        FrozenDictionary<string, Schema> membersByName,
        Schema? target,
        Lazy<Schema>? lazyTarget,
        Schema? listMember,
        Schema? mapKey,
        Schema? mapValue
    )
    {
        Id = id;
        Kind = kind;
        this.traits = traits;
        Members = members;
        this.membersByName = membersByName;
        this.target = target;
        this.lazyTarget = lazyTarget;
        ListMember = listMember;
        MapKey = mapKey;
        MapValue = mapValue;
    }

    public ShapeId Id { get; }

    public ShapeKind Kind { get; }

    /// <summary>Traits applied to this shape, keyed by trait id.</summary>
    public IReadOnlyDictionary<ShapeId, Trait> Traits => traits;

    /// <summary>
    /// Member schemas in declaration order. Non-empty only for <see cref="ShapeKind.Structure"/>
    /// and <see cref="ShapeKind.Union"/> shapes.
    /// </summary>
    public IReadOnlyList<Schema> Members { get; }

    /// <summary>
    /// Target shape for member schemas (<see cref="IsMember"/> is <c>true</c>); <c>null</c> otherwise.
    /// </summary>
    public Schema? Target => target ?? lazyTarget?.Value;

    /// <summary>Element member schema for <see cref="ShapeKind.List"/> / <see cref="ShapeKind.Set"/>.</summary>
    public Schema? ListMember { get; }

    /// <summary>Key member schema for <see cref="ShapeKind.Map"/>.</summary>
    public Schema? MapKey { get; }

    /// <summary>Value member schema for <see cref="ShapeKind.Map"/>.</summary>
    public Schema? MapValue { get; }

    public bool IsMember => Id.IsMember;

    public string? MemberName => Id.MemberName;

    public Schema? GetMember(string memberName)
    {
        ArgumentNullException.ThrowIfNull(memberName);
        return membersByName.TryGetValue(memberName, out var member) ? member : null;
    }

    public Trait? GetTrait(ShapeId id)
    {
        return traits.TryGetValue(id, out var trait) ? trait : null;
    }

    public bool HasTrait(ShapeId id) => traits.ContainsKey(id);

    /// <summary>Create a schema for a simple (non-aggregate) shape.</summary>
    public static Schema CreateSimple(ShapeId id, ShapeKind kind, IEnumerable<Trait>? traits = null)
    {
        return new Schema(
            id,
            kind,
            BuildTraits(traits),
            members: [],
            membersByName: FrozenDictionary<string, Schema>.Empty,
            target: null,
            lazyTarget: null,
            listMember: null,
            mapKey: null,
            mapValue: null
        );
    }

    /// <summary>Create a schema for a structure shape.</summary>
    public static Schema CreateStructure(
        ShapeId id,
        IEnumerable<Schema> members,
        IEnumerable<Trait>? traits = null
    ) => CreateAggregate(id, ShapeKind.Structure, members, traits);

    /// <summary>Create a schema for a union shape.</summary>
    public static Schema CreateUnion(
        ShapeId id,
        IEnumerable<Schema> members,
        IEnumerable<Trait>? traits = null
    ) => CreateAggregate(id, ShapeKind.Union, members, traits);

    /// <summary>Create a schema for a list shape.</summary>
    public static Schema CreateList(ShapeId id, Schema member, IEnumerable<Trait>? traits = null) =>
        CreateCollection(id, ShapeKind.List, member, traits);

    /// <summary>Create a schema for a set shape.</summary>
    public static Schema CreateSet(ShapeId id, Schema member, IEnumerable<Trait>? traits = null) =>
        CreateCollection(id, ShapeKind.Set, member, traits);

    private static Schema CreateCollection(
        ShapeId id,
        ShapeKind kind,
        Schema member,
        IEnumerable<Trait>? traits
    )
    {
        ArgumentNullException.ThrowIfNull(member);
        return new Schema(
            id,
            kind,
            BuildTraits(traits),
            members: [],
            membersByName: FrozenDictionary<string, Schema>.Empty,
            target: null,
            lazyTarget: null,
            listMember: member,
            mapKey: null,
            mapValue: null
        );
    }

    /// <summary>Create a schema for a map shape.</summary>
    public static Schema CreateMap(
        ShapeId id,
        Schema key,
        Schema value,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        return new Schema(
            id,
            ShapeKind.Map,
            BuildTraits(traits),
            members: [],
            membersByName: FrozenDictionary<string, Schema>.Empty,
            target: null,
            lazyTarget: null,
            listMember: null,
            mapKey: key,
            mapValue: value
        );
    }

    /// <summary>
    /// Create a member schema. The member id MUST include a member name. The target is provided as a factory to support recursive schemas.
    /// </summary>
    public static Schema CreateMember(
        ShapeId memberId,
        Func<Schema> targetFactory,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentNullException.ThrowIfNull(targetFactory);
        if (!memberId.IsMember)
        {
            throw new ArgumentException(
                $"Member schema id must include a member name; got '{memberId}'.",
                nameof(memberId)
            );
        }

        return new Schema(
            memberId,
            ShapeKind.Member,
            BuildTraits(traits),
            members: [],
            membersByName: FrozenDictionary<string, Schema>.Empty,
            target: null,
            lazyTarget: new Lazy<Schema>(() =>
            {
                var target = targetFactory();
                ArgumentNullException.ThrowIfNull(target);
                return target;
            }),
            listMember: null,
            mapKey: null,
            mapValue: null
        );
    }

    /// <summary>
    /// Create a member schema with a direct target (non-recursive, for convenience).
    /// </summary>
    public static Schema CreateMember(
        ShapeId memberId,
        Schema target,
        IEnumerable<Trait>? traits = null
    ) => CreateMember(memberId, () => target, traits);

    private static Schema CreateAggregate(
        ShapeId id,
        ShapeKind kind,
        IEnumerable<Schema> members,
        IEnumerable<Trait>? traits
    )
    {
        ArgumentNullException.ThrowIfNull(members);
        var memberList = members.ToArray();
        foreach (var m in memberList)
        {
            if (!m.IsMember)
            {
                throw new ArgumentException(
                    $"Schema for {id} contains non-member entry '{m.Id}'.",
                    nameof(members)
                );
            }
        }

        return new Schema(
            id,
            kind,
            BuildTraits(traits),
            members: memberList,
            membersByName: memberList.ToFrozenDictionary(
                m => m.MemberName!,
                StringComparer.Ordinal
            ),
            target: null,
            lazyTarget: null,
            listMember: null,
            mapKey: null,
            mapValue: null
        );
    }

    private static FrozenDictionary<ShapeId, Trait> BuildTraits(IEnumerable<Trait>? traits)
    {
        if (traits is null)
        {
            return FrozenDictionary<ShapeId, Trait>.Empty;
        }

        return traits.ToFrozenDictionary(t => t.Id);
    }

    public override string ToString() => $"Schema({Id}, {Kind})";

    public Schema RequireTarget()
    {
        if (Target is not null)
        {
            return Target;
        }

        if (lazyTarget is null)
        {
            throw new InvalidOperationException($"Schema '{Id}' does not have a target shape.");
        }

        return target = lazyTarget.Value;
    }
}
