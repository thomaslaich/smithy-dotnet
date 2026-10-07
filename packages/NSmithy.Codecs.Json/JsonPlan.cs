using System.Text;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>What the JSON codec needs to know about one member: built once, read per value.</summary>
internal sealed class JsonMemberPlan
{
    public JsonMemberPlan(
        string name,
        string wireName,
        bool isRequired,
        Schema target,
        Func<ShapeId, Trait?> getTrait,
        JsonPlans plans
    )
    {
        Name = name;
        WireName = wireName;
        EncodedName = JsonEncodedText.Encode(wireName);
        Utf8Name = Encoding.UTF8.GetBytes(wireName);
        IsRequired = isRequired;
        Target = target;
        TimestampFormat = Json.TimestampFormat.Resolve(getTrait);
        Shape = plans.ForTarget(target);
    }

    /// <summary>The member's name in the model, used in error paths.</summary>
    public string Name { get; }

    public string WireName { get; }

    public JsonEncodedText EncodedName { get; }

    public byte[] Utf8Name { get; }

    public bool IsRequired { get; }

    public Schema Target { get; }

    public string TimestampFormat { get; }

    /// <summary>The plan of the member's target when it is an aggregate.</summary>
    public JsonShapePlan? Shape { get; }
}

/// <summary>The JSON codec's plan for one aggregate shape, indexed by member position.</summary>
internal sealed class JsonShapePlan(ShapeKind kind, bool sparse)
{
    public ShapeKind Kind { get; } = kind;

    /// <summary>Whether a list or map may hold nulls.</summary>
    public bool Sparse { get; } = sparse;

    public JsonMemberPlan[] Members { get; set; } = [];

    /// <summary>Members a projection excludes; null when every member is written and read.</summary>
    public bool[]? Excluded { get; init; }

    /// <summary>The discriminator property of an <c>@alloy#discriminated</c> union.</summary>
    public string? Discriminator { get; set; }

    /// <summary>The <c>@alloy#jsonUnknown</c> case of an open union, or -1.</summary>
    public int UnknownCase { get; set; } = -1;

    public bool IsOpenUnion => Discriminator is not null || UnknownCase >= 0;

    public bool IsIncluded(int member) => Excluded is null || !Excluded[member];

    public int IndexOf(JsonProperty property)
    {
        for (var index = 0; index < Members.Length; index++)
        {
            if (property.NameEquals(Members[index].Utf8Name))
            {
                return index;
            }
        }

        return -1;
    }

    public int IndexOf(string wireName)
    {
        for (var index = 0; index < Members.Length; index++)
        {
            if (string.Equals(Members[index].WireName, wireName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>A copy that excludes every member <paramref name="include"/> rejects.</summary>
    public JsonShapePlan Project(Func<string, bool> include) =>
        new(Kind, Sparse)
        {
            Members = Members,
            Excluded = [.. Members.Select(member => !include(member.Name))],
        };
}

/// <summary>Builds and memoizes plans for one codec configuration.</summary>
internal sealed class JsonPlans(bool honorJsonNameTrait)
{
    private static readonly ShapeId SparseTrait = new("smithy.api", "sparse");
    private static readonly ShapeId JsonNameTrait = new("smithy.api", "jsonName");
    private static readonly ShapeId AlloyDiscriminatedTrait = new("alloy", "discriminated");
    private static readonly ShapeId AlloyJsonUnknownTrait = new("alloy", "jsonUnknown");

    private readonly Dictionary<Schema, JsonShapePlan> plans = new(
        ReferenceEqualityComparer.Instance
    );

    /// <summary>The plan for a top-level value, which is no member of anything.</summary>
    public JsonMemberPlan ForRoot(Schema schema, IReadOnlyDictionary<ShapeId, Trait>? traits) =>
        new(
            schema.Id.Name,
            schema.Id.Name,
            isRequired: true,
            schema,
            id =>
                traits is not null && traits.TryGetValue(id, out var trait)
                    ? trait
                    : schema.Resolved.GetTrait(id),
            this
        );

    /// <summary>The plan for a member's target, or null when the target is a simple shape.</summary>
    public JsonShapePlan? ForTarget(Schema target)
    {
        var resolved = Unwrap(target);
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
        var plan = new JsonShapePlan(resolved.Kind, resolved.HasTrait(SparseTrait));
        plans.Add(resolved, plan);
        plan.Members = resolved switch
        {
            IStructSchema structure => [.. structure.Members.Select(Member)],
            IListSchema list => [Member(list.ElementMember)],
            IMapSchema map => [Member(map.KeyMember), Member(map.ValueMember)],
            IUnionSchema union => [.. union.Cases.Select(Case)],
            _ => [],
        };
        if (resolved is IUnionSchema unionSchema)
        {
            if (resolved.Traits.TryGetValue(AlloyDiscriminatedTrait, out var discriminated))
            {
                plan.Discriminator = discriminated.Value.AsString();
            }

            for (var index = 0; index < unionSchema.Cases.Count; index++)
            {
                if (unionSchema.Cases[index].Traits.ContainsKey(AlloyJsonUnknownTrait))
                {
                    plan.UnknownCase = index;
                }
            }
        }

        return plan;
    }

    private JsonMemberPlan Member(IMemberSchema member) =>
        new(
            member.Name,
            WireName(member.MemberTraits, member.Name),
            member.IsRequired,
            member.Target,
            member.GetTrait,
            this
        );

    private JsonMemberPlan Case(IUnionCaseSchema @case) =>
        new(
            @case.Name,
            WireName(@case.Traits, @case.Name),
            isRequired: true,
            @case.Target,
            id =>
                @case.Traits.TryGetValue(id, out var trait)
                    ? trait
                    : @case.Target.Resolved.GetTrait(id),
            this
        );

    private string WireName(IReadOnlyDictionary<ShapeId, Trait> traits, string name) =>
        honorJsonNameTrait && traits.TryGetValue(JsonNameTrait, out var trait)
            ? trait.Value.AsString()
            : name;

    private static Schema Unwrap(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }
}
