using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.AwsQuery;

internal enum QueryProtocolKind
{
    AwsQuery,
    Ec2Query,
}

/// <summary>
/// Serializes an operation input as an AWS Query / EC2 Query form body. The plan is compiled once
/// per operation; a request only walks it.
/// </summary>
internal sealed class QueryFormSerializer<T>
{
    private readonly string action;
    private readonly string version;
    private readonly Schema<T> schema;
    private readonly QueryMemberPlan root;

    public QueryFormSerializer(
        QueryProtocolKind kind,
        string action,
        string version,
        Schema<T> schema
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(schema);
        this.action = action;
        this.version = version;
        this.schema = schema;
        root = new QueryPlans(kind).ForMember(schema, memberTraits: null, name: "");
    }

    public byte[] Serialize(T value)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("Action", action),
            new("Version", version),
        };
        var serializer = QueryFormShapeSerializer.ForRoot(parameters, root);
        schema.Write(MemberIndex.Root, value, ref serializer);

        return Encoding.UTF8.GetBytes(
            string.Join(
                "&",
                parameters.Select(parameter =>
                    $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"
                )
            )
        );
    }
}

/// <summary>
/// Writes form parameters for the values of one structure, list, or map, each under its name
/// joined to the aggregate's prefix. A scalar at the root has no name to be written under.
/// </summary>
internal struct QueryFormShapeSerializer : IShapeSerializer
{
    private enum Context
    {
        Root,
        Struct,
        List,
        Map,
    }

    private readonly List<KeyValuePair<string, string>> parameters;
    private readonly string prefix;
    private readonly QueryMemberPlan plan;
    private readonly Context context;

    // The position of the next list element or map entry, from one; and the open entry's prefix.
    private int index;
    private string entryPrefix = "";

    private QueryFormShapeSerializer(
        List<KeyValuePair<string, string>> parameters,
        string prefix,
        QueryMemberPlan plan,
        Context context
    )
    {
        this.parameters = parameters;
        this.prefix = prefix;
        this.plan = plan;
        this.context = context;
    }

    public static QueryFormShapeSerializer ForRoot(
        List<KeyValuePair<string, string>> parameters,
        QueryMemberPlan root
    ) => new(parameters, "", root, Context.Root);

    /// <summary>The plan of the value <paramref name="member"/> is written as, and its name.</summary>
    private (QueryMemberPlan Plan, string Name) Next(int member)
    {
        switch (context)
        {
            case Context.Struct:
                var memberPlan = plan.Members![member];
                return (memberPlan, Join(prefix, memberPlan.Name));
            case Context.List:
                index++;
                return (
                    plan.Element!,
                    Join(prefix, plan.ItemName, index.ToString(CultureInfo.InvariantCulture))
                );
            case Context.Map:
                return (plan.Value!, Join(entryPrefix, plan.ValueName));
            default:
                return (plan, prefix);
        }
    }

    private void Add(int member, string text)
    {
        var (_, name) = Next(member);
        if (name.Length > 0)
        {
            parameters.Add(new(name, text));
        }
    }

    public readonly bool WritesDefault(int member) => false;

    public void WriteNull(int member)
    {
        // A null element still has a position in its list.
        if (context == Context.List)
        {
            index++;
        }
    }

    public void WriteBoolean(int member, bool value) => Add(member, value ? "true" : "false");

    public void WriteByte(int member, sbyte value) =>
        Add(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteShort(int member, short value) =>
        Add(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteInteger(int member, int value) =>
        Add(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteLong(int member, long value) =>
        Add(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteFloat(int member, float value) =>
        Add(member, value.ToString("R", CultureInfo.InvariantCulture));

    public void WriteDouble(int member, double value) =>
        Add(member, value.ToString("R", CultureInfo.InvariantCulture));

    public void WriteBigInteger(int member, BigInteger value) =>
        Add(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteBigDecimal(int member, decimal value) =>
        Add(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteString(int member, string value)
    {
        // A map entry opens with its key.
        if (context == Context.Map && member == 0)
        {
            index++;
            entryPrefix = Join(
                prefix,
                plan.EntryName,
                index.ToString(CultureInfo.InvariantCulture)
            );
            parameters.Add(new(Join(entryPrefix, plan.KeyName), value));
            return;
        }

        Add(member, value);
    }

    public void WriteBlob(int member, byte[] value) => Add(member, Convert.ToBase64String(value));

    public void WriteTimestamp(int member, DateTimeOffset value)
    {
        var (target, name) = Next(member);
        if (name.Length > 0)
        {
            parameters.Add(
                new(
                    name,
                    target.TimestampFormat switch
                    {
                        "epoch-seconds" => FormatEpochSeconds(value),
                        "http-date" => value
                            .ToUniversalTime()
                            .ToString("r", CultureInfo.InvariantCulture),
                        _ => FormatRfc3339(value),
                    }
                )
            );
        }
    }

    public void WriteDocument(int member, Document value) => throw Unsupported(member);

    public void WriteStringEnum(int member, string value) => Add(member, value);

    public void WriteIntEnum(int member, int value) =>
        Add(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteStream(int member, Stream value) => throw Unsupported(member);

    public void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    ) => throw Unsupported(member);

    public void WriteStruct<T>(int member, T value, IStructSchema<T> schema)
    {
        var (target, name) = Next(member);
        var members = new QueryFormShapeSerializer(parameters, name, target, Context.Struct);
        schema.SerializeMembers(value, ref members);
    }

    public void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        var (target, name) = Next(member);
        var elements = new QueryFormShapeSerializer(parameters, name, target, Context.List);
        schema.SerializeElements(value, ref elements);
        if (elements.index == 0 && target.WriteEmptyMarker)
        {
            parameters.Add(new(name, string.Empty));
        }
    }

    public void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        var (target, name) = Next(member);
        if (target.MapUnsupported)
        {
            // An empty map is still nothing to write.
            if (schema.GetEntries(value).Any())
            {
                throw new NotSupportedException("EC2 Query does not support map input shapes.");
            }

            return;
        }

        var entries = new QueryFormShapeSerializer(parameters, name, target, Context.Map);
        schema.SerializeEntries(value, ref entries);
    }

    public void WriteUnion<T>(int member, T value, UnionSchema<T> schema) =>
        throw Unsupported(member);

    private NotSupportedException Unsupported(int member)
    {
        var target = Next(member).Plan.Target;
        return new NotSupportedException(
            $"AWS Query does not support schema kind '{target.Kind}' on shape '{target.Id}'."
        );
    }

    private static string Join(string first, params string?[] parts)
    {
        var values = parts.Where(part => !string.IsNullOrEmpty(part));
        return first.Length == 0 ? string.Join('.', values) : string.Join('.', [first, .. values]);
    }

    private static string FormatRfc3339(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatEpochSeconds(DateTimeOffset value)
    {
        var unixSeconds = value.ToUnixTimeSeconds();
        var fractionalTicks = value.ToUniversalTime().Ticks % TimeSpan.TicksPerSecond;
        if (fractionalTicks == 0)
        {
            return unixSeconds.ToString(CultureInfo.InvariantCulture);
        }

        var fractional = ((decimal)fractionalTicks / TimeSpan.TicksPerSecond).ToString(
            "0.################",
            CultureInfo.InvariantCulture
        );
        return $"{unixSeconds}{fractional[1..]}";
    }
}
