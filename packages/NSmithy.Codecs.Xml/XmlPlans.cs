using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

/// <summary>What the XML codec needs to know about one member: built once, read per value.</summary>
internal sealed class XmlMemberPlan(
    string name,
    string elementName,
    bool isRequired,
    Schema target,
    IReadOnlyDictionary<ShapeId, Trait> memberTraits,
    XmlPlans plans
)
{
    private static readonly ShapeId XmlAttributeTrait = new("smithy.api", "xmlAttribute");
    private static readonly ShapeId XmlFlattenedTrait = new("smithy.api", "xmlFlattened");
    private static readonly ShapeId XmlNamespaceTrait = new("smithy.api", "xmlNamespace");
    private static readonly ShapeId TimestampFormatTrait = new("smithy.api", "timestampFormat");

    /// <summary>The member's name in the model, used in error messages.</summary>
    public string Name { get; } = name;

    /// <summary>The element (or attribute) name: <c>@xmlName</c>, or the member's own name.</summary>
    public string ElementName { get; } = elementName;

    public bool IsRequired { get; } = isRequired;

    public Schema Target { get; } = target;

    public bool IsAttribute { get; } = memberTraits.ContainsKey(XmlAttributeTrait);

    /// <summary>
    /// Whether a list or map member repeats its items directly in the enclosing element rather
    /// than inside an element of its own.
    /// </summary>
    public bool IsFlattened { get; } = memberTraits.ContainsKey(XmlFlattenedTrait);

    public XmlNamespace? Namespace { get; } =
        XmlTraits.GetXmlNamespace(target.Resolved, memberTraits);

    public string TimestampFormat { get; } =
        (
            memberTraits.TryGetValue(TimestampFormatTrait, out var format)
                ? format
                : target.Resolved.GetTrait(TimestampFormatTrait)
        )?.Value.AsString() ?? "date-time";

    /// <summary>The plan of the member's target when it is an aggregate.</summary>
    public XmlShapePlan? Shape { get; } = plans.ForTarget(target);
}

/// <summary>The XML codec's plan for one aggregate shape, indexed by member position.</summary>
internal sealed class XmlShapePlan(ShapeKind kind)
{
    public ShapeKind Kind { get; } = kind;

    public XmlMemberPlan[] Members { get; set; } = [];

    /// <summary>Members a projection excludes; null when every member is written and read.</summary>
    public bool[]? Excluded { get; init; }

    public bool IsIncluded(int member) => Excluded is null || !Excluded[member];

    public int IndexOfElement(string localName)
    {
        for (var index = 0; index < Members.Length; index++)
        {
            if (string.Equals(Members[index].ElementName, localName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>A copy that excludes every member <paramref name="include"/> rejects.</summary>
    public XmlShapePlan Project(Func<string, bool> include) =>
        new(Kind)
        {
            Members = Members,
            Excluded = [.. Members.Select(member => !include(member.Name))],
        };
}

/// <summary>Builds and memoizes plans for one codec.</summary>
internal sealed class XmlPlans
{
    private static readonly ShapeId XmlNameTrait = new("smithy.api", "xmlName");

    private readonly Dictionary<Schema, XmlShapePlan> plans = new(
        ReferenceEqualityComparer.Instance
    );

    /// <summary>The plan for a top-level value, which is no member of anything.</summary>
    public XmlMemberPlan ForRoot(Schema schema, IReadOnlyDictionary<ShapeId, Trait>? traits) =>
        new(
            schema.Id.Name,
            schema.Id.Name,
            isRequired: true,
            schema,
            traits ?? new Dictionary<ShapeId, Trait>(),
            this
        );

    /// <summary>The plan for a member's target, or null when the target is a simple shape.</summary>
    public XmlShapePlan? ForTarget(Schema target)
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
        var plan = new XmlShapePlan(resolved.Kind);
        plans.Add(resolved, plan);
        plan.Members = resolved switch
        {
            IStructSchema structure =>
            [
                .. structure.Members.Select(member => Member(member, member.Name)),
            ],
            IListSchema list => [Member(list.ElementMember, "member")],
            IMapSchema map => [Member(map.KeyMember, "key"), Member(map.ValueMember, "value")],
            // A union case's element is named for the case; @xmlName does not rename it.
            IUnionSchema union =>
            [
                .. union.Cases.Select(@case => new XmlMemberPlan(
                    @case.Name,
                    @case.Name,
                    isRequired: true,
                    @case.Target,
                    @case.MemberTraits,
                    this
                )),
            ],
            _ => [],
        };
        return plan;
    }

    private XmlMemberPlan Member(MemberSchema member, string fallbackName) =>
        new(
            member.Name,
            member.MemberTraits.TryGetValue(XmlNameTrait, out var name) && name.HasValue
                ? name.Value.AsString()
                : fallbackName,
            member.IsRequired,
            member.Target,
            member.MemberTraits,
            this
        );
}
