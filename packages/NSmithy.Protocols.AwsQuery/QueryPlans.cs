using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.AwsQuery;

/// <summary>
/// How one value is named in the form: a structure member by its (query) name, a list element by
/// its position, a map value under its entry. A list's and map's naming depends on the member's
/// traits, so each member gets its own plan; only a structure's members are shared.
/// </summary>
internal sealed class QueryMemberPlan
{
    public required string Name { get; init; }

    public required Schema Target { get; init; }

    public string? TimestampFormat { get; init; }

    /// <summary>The members of a structure target, indexed by member position.</summary>
    public QueryMemberPlan[]? Members { get; set; }

    /// <summary>The element of a list target.</summary>
    public QueryMemberPlan? Element { get; init; }

    /// <summary>The segment that precedes each list element's index, absent once flattened.</summary>
    public string? ItemName { get; init; }

    /// <summary>Whether an empty list is still written, as its name with an empty value.</summary>
    public bool WriteEmptyMarker { get; init; }

    /// <summary>The value of a map target.</summary>
    public QueryMemberPlan? Value { get; init; }

    public string? EntryName { get; init; }

    public string KeyName { get; init; } = "key";

    public string ValueName { get; init; } = "value";

    /// <summary>EC2 Query has no map form: a map target with entries cannot be written.</summary>
    public bool MapUnsupported { get; init; }
}

/// <summary>Builds query plans, memoizing each structure's members so recursion terminates.</summary>
internal sealed class QueryPlans(QueryProtocolKind kind)
{
    private static readonly ShapeId Ec2QueryName = ShapeId.Parse("aws.protocols#ec2QueryName");
    private static readonly ShapeId TimestampFormat = ShapeId.Parse("smithy.api#timestampFormat");
    private static readonly ShapeId XmlFlattened = ShapeId.Parse("smithy.api#xmlFlattened");
    private static readonly ShapeId XmlName = ShapeId.Parse("smithy.api#xmlName");

    private readonly Dictionary<Schema, QueryMemberPlan[]> structures = new(
        ReferenceEqualityComparer.Instance
    );

    public QueryMemberPlan ForMember(
        Schema target,
        IReadOnlyDictionary<ShapeId, Trait>? memberTraits,
        string name
    )
    {
        var resolved = target.Resolved is INullableSchema nullable
            ? nullable.Target.Resolved
            : target.Resolved;
        var flattened =
            kind == QueryProtocolKind.Ec2Query || memberTraits?.ContainsKey(XmlFlattened) == true;

        switch (resolved)
        {
            case IStructSchema structure:
                return new QueryMemberPlan
                {
                    Name = name,
                    Target = resolved,
                    Members = Members(resolved, structure),
                };
            case IListSchema list:
                return new QueryMemberPlan
                {
                    Name = name,
                    Target = resolved,
                    Element = ForMember(list.Element, list.ElementMember.MemberTraits, ""),
                    ItemName =
                        kind == QueryProtocolKind.AwsQuery && !flattened
                            ? StringTrait(list.ElementMember.MemberTraits, XmlName) ?? "member"
                            : null,
                    WriteEmptyMarker = kind == QueryProtocolKind.AwsQuery && !flattened,
                };
            case IMapSchema map when kind == QueryProtocolKind.Ec2Query:
                return new QueryMemberPlan
                {
                    Name = name,
                    Target = resolved,
                    MapUnsupported = true,
                };
            case IMapSchema map:
                return new QueryMemberPlan
                {
                    Name = name,
                    Target = resolved,
                    Value = ForMember(map.Value, map.ValueMember.MemberTraits, ""),
                    EntryName = memberTraits?.ContainsKey(XmlFlattened) == true ? null : "entry",
                    KeyName = StringTrait(map.KeyMember.MemberTraits, XmlName) ?? "key",
                    ValueName = StringTrait(map.ValueMember.MemberTraits, XmlName) ?? "value",
                };
            default:
                return new QueryMemberPlan
                {
                    Name = name,
                    Target = resolved,
                    TimestampFormat =
                        resolved.Kind == ShapeKind.Timestamp
                            ? CheckTimestampFormat(
                                StringTrait(memberTraits, TimestampFormat)
                                    ?? (
                                        resolved.GetTrait(TimestampFormat)
                                            is { HasValue: true } trait
                                            ? trait.Value.AsString()
                                            : null
                                    )
                            )
                            : null,
                };
        }
    }

    private QueryMemberPlan[] Members(Schema resolved, IStructSchema structure)
    {
        if (structures.TryGetValue(resolved, out var existing))
        {
            return existing;
        }

        var members = new QueryMemberPlan[structure.Members.Count];
        structures.Add(resolved, members);
        for (var index = 0; index < members.Length; index++)
        {
            var member = structure.Members[index];
            members[index] = ForMember(member.Target, member.MemberTraits, MemberName(member));
        }

        return members;
    }

    private string MemberName(MemberSchema member)
    {
        if (kind == QueryProtocolKind.AwsQuery)
        {
            return StringTrait(member.MemberTraits, XmlName) ?? member.Name;
        }

        return StringTrait(member.MemberTraits, Ec2QueryName)
            ?? UppercaseFirst(StringTrait(member.MemberTraits, XmlName) ?? member.Name);
    }

    private static string? CheckTimestampFormat(string? format) =>
        format is null or "date-time" or "epoch-seconds" or "http-date"
            ? format
            : throw new NotSupportedException(
                $"Timestamp format '{format}' is not supported by AWS Query."
            );

    private static string? StringTrait(IReadOnlyDictionary<ShapeId, Trait>? traits, ShapeId id) =>
        traits is not null && traits.TryGetValue(id, out var trait) && trait.HasValue
            ? trait.Value.AsString()
            : null;

    private static string UppercaseFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
