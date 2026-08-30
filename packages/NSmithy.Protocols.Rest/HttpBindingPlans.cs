using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

// One plan per bound member, compiled once from the member schema and its value codec. A plan
// knows its member's value type; the binding that holds it only knows the container and builder,
// so the per-request loops stay monomorphic without ever boxing a value.

internal interface IHttpLabelWriter<in T>
{
    void Write(HttpUriBuilder uri, T container);
}

internal interface IHttpLabelReader<in TBuilder>
{
    string Name { get; }

    bool IsRequired { get; }

    void Read(TBuilder builder, string value);
}

/// <summary>Where a request header lands: the transport owns the content headers.</summary>
internal enum HeaderSlot
{
    Headers,
    ContentType,
    ContentHeaders,
}

internal interface IHttpHeaderWriter<in T>
{
    string Name { get; }

    HeaderSlot Slot { get; }

    string? Format(T container);
}

internal interface IHttpHeaderReader<in TBuilder>
{
    string Name { get; }

    string MemberName { get; }

    bool IsRequired { get; }

    void Read(TBuilder builder, string value);
}

internal interface IHttpQueryWriter<in T>
{
    void Write(HttpUriBuilder uri, T container);
}

internal interface IHttpQueryReader<in TBuilder>
{
    string Name { get; }

    string MemberName { get; }

    bool IsRequired { get; }

    void Read(TBuilder builder, IReadOnlyList<string> values);
}

internal interface IHttpPrefixHeaderWriter<in T>
{
    void Write(IDictionary<string, IReadOnlyList<string>> headers, T container);
}

internal interface IHttpPrefixHeaderReader<in TBuilder>
{
    void Read(TBuilder builder, IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> headers);
}

internal interface IHttpQueryParamsWriter<in T>
{
    void Write(HttpUriBuilder uri, T container, HashSet<string> excludedNames);
}

internal interface IHttpQueryParamsReader<in TBuilder>
{
    void Read(TBuilder builder, Dictionary<string, IReadOnlyList<string>> query);
}

/// <summary>
/// The plan for a map-valued binding, which serves as either side of <c>@httpPrefixHeaders</c> or
/// <c>@httpQueryParams</c>.
/// </summary>
internal interface IMapBindingPlan<in T, in TBuilder>
    : IHttpPrefixHeaderWriter<T>,
        IHttpPrefixHeaderReader<TBuilder>,
        IHttpQueryParamsWriter<T>,
        IHttpQueryParamsReader<TBuilder>;

internal interface IHttpStatusCodeWriter<in T>
{
    int? Get(T container);
}

internal interface IHttpStatusCodeReader<in TBuilder>
{
    void Read(TBuilder builder, int statusCode);
}

internal sealed class LabelPlan<T, TBuilder, TValue>(
    IMemberSchema<T, TBuilder, TValue> member,
    IHttpValueCodec<TValue> codec,
    bool greedy
) : IHttpLabelWriter<T>, IHttpLabelReader<TBuilder>
{
    private readonly string placeholder = greedy
        ? "{" + member.Name + "+}"
        : "{" + member.Name + "}";

    public string Name => member.Name;

    public bool IsRequired => member.IsRequired;

    public void Write(HttpUriBuilder uri, T container)
    {
        if (member.GetValue(container) is not { } value)
        {
            throw new InvalidOperationException(
                $"HTTP label member '{member.Name}' cannot be null."
            );
        }

        var text = codec.Format(value);
        uri.ReplaceLabel(
            placeholder,
            greedy ? HttpValueText.EscapeGreedyLabel(text) : Uri.EscapeDataString(text)
        );
    }

    public void Read(TBuilder builder, string value) =>
        member.SetValue(builder, codec.Parse(value));
}

internal sealed class HeaderPlan<T, TBuilder, TValue>(
    IMemberSchema<T, TBuilder, TValue> member,
    IHttpValueCodec<TValue> codec,
    string name
) : IHttpHeaderWriter<T>, IHttpHeaderReader<TBuilder>
{
    public string Name => name;

    public string MemberName => member.Name;

    public bool IsRequired => member.IsRequired;

    public HeaderSlot Slot { get; } =
        string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)
            ? HeaderSlot.ContentType
        : string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase)
            ? HeaderSlot.ContentHeaders
        : HeaderSlot.Headers;

    public string? Format(T container) =>
        member.GetValue(container) is { } value ? codec.FormatHeader(value) : null;

    public void Read(TBuilder builder, string value) =>
        member.SetValue(builder, codec.ParseHeader(value));
}

internal sealed class QueryPlan<T, TBuilder, TValue>(
    IMemberSchema<T, TBuilder, TValue> member,
    IHttpValueCodec<TValue> codec,
    string name
) : IHttpQueryWriter<T>, IHttpQueryReader<TBuilder>
{
    public string Name => name;

    public string MemberName => member.Name;

    public bool IsRequired => member.IsRequired;

    public void Write(HttpUriBuilder uri, T container)
    {
        if (member.GetValue(container) is { } value)
        {
            codec.AppendQuery(uri, name, value);
        }
    }

    public void Read(TBuilder builder, IReadOnlyList<string> values) =>
        member.SetValue(builder, codec.ParseMany(values));
}

/// <summary>
/// An <c>@httpPrefixHeaders</c> or <c>@httpQueryParams</c> member targets a map; the map's own
/// types only come into scope by visiting it, which is what this does.
/// </summary>
internal sealed class MapBindingPlanCompiler<T, TBuilder, TValue>(
    IMemberSchema<T, TBuilder, TValue> member,
    string prefix
) : PartialSchemaVisitor<IMapBindingPlan<T, TBuilder>>
{
    public override IMapBindingPlan<T, TBuilder> VisitMap<TDictionary, TMapValue, TMapBuilder>(
        IMapSchema<TDictionary, TMapValue, TMapBuilder> schema
    ) =>
        new MapBindingPlan<T, TBuilder, TDictionary, TMapValue, TMapBuilder>(
            (IMemberSchema<T, TBuilder, TDictionary>)(object)member,
            schema,
            HttpBindingCompiler.Compile(schema.ValueSchema, memberTraits: null),
            prefix
        );

    protected override IMapBindingPlan<T, TBuilder> VisitDefault(Schema schema) =>
        throw new InvalidOperationException(
            $"HTTP binding member '{member.Name}' must target a map schema."
        );
}

internal sealed class MapBindingPlan<T, TBuilder, TDictionary, TValue, TMapBuilder>(
    IMemberSchema<T, TBuilder, TDictionary> member,
    IMapSchema<TDictionary, TValue, TMapBuilder> map,
    IHttpValueCodec<TValue> value,
    string prefix
) : IMapBindingPlan<T, TBuilder>
{
    public void Write(IDictionary<string, IReadOnlyList<string>> headers, T container)
    {
        if (member.GetValue(container) is not { } entries)
        {
            return;
        }

        foreach (var entry in map.GetEntries(entries))
        {
            if (entry.Value is null)
            {
                continue;
            }

            var headerName = prefix + entry.Key;
            if (!headers.ContainsKey(headerName))
            {
                headers[headerName] = [value.Format(entry.Value)];
            }
        }
    }

    public void Read(
        TBuilder builder,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> headers
    )
    {
        var entries = map.CreateTypedBuilder();
        foreach (var header in headers)
        {
            if (
                !header.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || (prefix.Length == 0 && IsTransportManagedHeader(header.Key))
                || header.Value.Count == 0
            )
            {
                continue;
            }

            map.Add(entries, header.Key[prefix.Length..], value.Parse(header.Value[0]));
        }

        member.SetValue(builder, map.Build(entries));
    }

    public void Write(HttpUriBuilder uri, T container, HashSet<string> excludedNames)
    {
        if (member.GetValue(container) is not { } entries)
        {
            return;
        }

        foreach (var entry in map.GetEntries(entries))
        {
            if (entry.Value is not null && !excludedNames.Contains(entry.Key))
            {
                value.AppendQuery(uri, entry.Key, entry.Value);
            }
        }
    }

    public void Read(TBuilder builder, Dictionary<string, IReadOnlyList<string>> query)
    {
        var entries = map.CreateTypedBuilder();
        foreach (var entry in query)
        {
            if (entry.Value.Count > 0)
            {
                map.Add(entries, entry.Key, value.ParseMany(entry.Value));
            }
        }

        member.SetValue(builder, map.Build(entries));
    }

    private static bool IsTransportManagedHeader(string name) =>
        name.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);
}

/// <summary><c>@httpResponseCode</c> targets an integer, optional or not.</summary>
internal sealed class StatusCodePlan<T, TBuilder>
{
    internal static (
        IHttpStatusCodeWriter<T> Writer,
        IHttpStatusCodeReader<TBuilder> Reader
    ) Compile<TValue>(IMemberSchema<T, TBuilder, TValue> member)
    {
        if (member is IMemberSchema<T, TBuilder, int> required)
        {
            var plan = new Required(required);
            return (plan, plan);
        }

        if (member is IMemberSchema<T, TBuilder, int?> optional)
        {
            var plan = new Optional(optional);
            return (plan, plan);
        }

        throw new InvalidOperationException(
            $"@httpResponseCode member '{member.Name}' must target an integer."
        );
    }

    private sealed class Required(IMemberSchema<T, TBuilder, int> member)
        : IHttpStatusCodeWriter<T>,
            IHttpStatusCodeReader<TBuilder>
    {
        public int? Get(T container) => member.GetValue(container);

        public void Read(TBuilder builder, int statusCode) => member.SetValue(builder, statusCode);
    }

    private sealed class Optional(IMemberSchema<T, TBuilder, int?> member)
        : IHttpStatusCodeWriter<T>,
            IHttpStatusCodeReader<TBuilder>
    {
        public int? Get(T container) => member.GetValue(container);

        public void Read(TBuilder builder, int statusCode) => member.SetValue(builder, statusCode);
    }
}

internal static class HttpBindingPlans
{
    internal static Schema UnwrapNullable(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }
}
