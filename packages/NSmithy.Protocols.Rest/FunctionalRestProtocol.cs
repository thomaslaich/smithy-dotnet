using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Functional;
using NSmithy.Http;

namespace NSmithy.Protocols.Rest;

public interface IFunctionalRestBodyFormat
{
    string ContentType { get; }

    string BlobContentType { get; }

    byte[] Serialize<T>(FunctionalSchema<T> schema, T value);

    T Deserialize<T>(FunctionalSchema<T> schema, byte[] content);

    byte[] Serialize<T>(FunctionalStructProjection<T> projection, T value);

    void ReadInto<T>(FunctionalStructProjection<T> projection, byte[] content, object builder);
}

public static class FunctionalRestProtocol
{
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        RestOperationBinding<TInput, TOutput> binding,
        TInput input,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(bodyFormat);

        var requestUri = BuildRequestUri(binding.UriTemplate, binding.LabelMembers, input!);

        foreach (var (member, queryName) in binding.QueryMembers)
            requestUri = AppendQuery(requestUri, queryName, member, input!);
        if (binding.QueryParamsMember is { } qpMember)
            requestUri = AppendQueryParams(requestUri, qpMember, input!);

        var request = new SmithyHttpRequest(binding.HttpMethod, requestUri);

        foreach (var (member, headerName) in binding.RequestHeaderMembers)
            AddHeader(request.Headers, headerName, member, input!);
        if (binding.RequestPrefixHeadersMember is { } phMember)
            AddPrefixedHeaders(request.Headers, phMember.Prefix, phMember.Member, input!);

        if (binding.InputPayloadMember is not null)
        {
            WritePayload(request, binding.InputPayloadMember, input!, bodyFormat);
            return request;
        }
        if (binding.InputBodyProjection is not null)
            WriteProjectionBody(request, binding.InputBodyProjection, input!, bodyFormat);

        return request;
    }

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TInput input,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return SerializeRequest(RestOperationBinding.From(operation), input, bodyFormat);
    }

    public static TInput DeserializeRequest<TInput, TOutput>(
        RestOperationBinding<TInput, TOutput> binding,
        SmithyHttpRequest request,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bodyFormat);

        var builder = binding.InputSchema.CreateBuilder();
        var labels = ExtractLabels(binding.UriTemplate, request.RequestUri);
        var query = ParseQuery(request.RequestUri);

        foreach (var member in binding.LabelMembers)
        {
            if (labels.TryGetValue(member.Name, out var labelValue))
                member.SetObject(builder, ParseHttpValue(member.Target, member.Traits, labelValue));
        }
        foreach (var (member, headerName) in binding.RequestHeaderMembers)
        {
            if (TryGetFirstHeader(request.Headers, headerName, out var header))
                member.SetObject(
                    builder,
                    ParseHttpBindingValue(member.Target, member.Traits, header)
                );
        }
        if (binding.RequestPrefixHeadersMember is { } reqPhMember)
            reqPhMember.Member.SetObject(
                builder,
                ReadPrefixedHeaders(reqPhMember.Member, request.Headers, reqPhMember.Prefix)
            );
        foreach (var (member, queryName) in binding.QueryMembers)
        {
            if (query.TryGetValue(queryName, out var values) && values.Count > 0)
                member.SetObject(
                    builder,
                    ParseHttpBindingValues(member.Target, member.Traits, values)
                );
        }
        if (binding.QueryParamsMember is { } qpMember)
            qpMember.SetObject(builder, ReadQueryParams(qpMember, query, binding.BoundQueryNames));
        if (binding.InputPayloadMember is { } payloadMember)
            ReadPayload(builder, payloadMember, request.Content, bodyFormat);
        else if (
            binding.InputBodyProjection is { } projection
            && request.Content is { Length: > 0 } content
        )
            bodyFormat.ReadInto(projection, content, builder);

        return (TInput)binding.InputSchema.BuildObject(builder);
    }

    public static TInput DeserializeRequest<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpRequest request,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return DeserializeRequest(RestOperationBinding.From(operation), request, bodyFormat);
    }

    public static SmithyHttpResponse SerializeResponse<TInput, TOutput>(
        RestOperationBinding<TInput, TOutput> binding,
        TOutput output,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(bodyFormat);

        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var statusCode = HttpStatusCode.OK;

        if (binding.ResponseCodeMember is { } codeMember)
        {
            var value = codeMember.GetObject(output!);
            if (value is not null)
                statusCode = (HttpStatusCode)
                    (int)ParseHttpValue(FunctionalSchemas.Integer, value.ToString()!)!;
        }
        foreach (var (member, headerName) in binding.ResponseHeaderMembers)
            AddHeader(headers, headerName, member, output!);
        if (binding.ResponsePrefixHeadersMember is { } respPhMember)
            AddPrefixedHeaders(headers, respPhMember.Prefix, respPhMember.Member, output!);

        byte[] content = [];
        if (binding.OutputPayloadMember is not null)
            content = SerializePayload(
                binding.OutputPayloadMember,
                output!,
                contentHeaders,
                bodyFormat
            );
        else if (binding.OutputBodyProjection is not null)
            content = SerializeProjectionBody(
                binding.OutputBodyProjection,
                output!,
                contentHeaders,
                bodyFormat
            );

        return new SmithyHttpResponse(statusCode, null, content, headers, contentHeaders);
    }

    public static SmithyHttpResponse SerializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        TOutput output,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return SerializeResponse(RestOperationBinding.From(operation), output, bodyFormat);
    }

    public static TOutput DeserializeResponse<TInput, TOutput>(
        RestOperationBinding<TInput, TOutput> binding,
        SmithyHttpResponse response,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(bodyFormat);

        var builder = binding.OutputSchema.CreateBuilder();

        if (binding.ResponseCodeMember is { } codeMember)
            codeMember.SetObject(
                builder,
                ParseHttpValue(
                    codeMember.Target,
                    ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                )
            );
        foreach (var (member, headerName) in binding.ResponseHeaderMembers)
        {
            if (
                TryGetFirstHeader(response.Headers, headerName, out var header)
                || TryGetFirstHeader(response.ContentHeaders, headerName, out header)
            )
            {
                member.SetObject(
                    builder,
                    ParseHttpBindingValue(member.Target, member.Traits, header)
                );
            }
        }
        if (binding.ResponsePrefixHeadersMember is { } respPhMember)
            respPhMember.Member.SetObject(
                builder,
                ReadPrefixedHeaders(respPhMember.Member, response.Headers, respPhMember.Prefix)
            );
        if (binding.OutputPayloadMember is { } payloadMember)
            ReadPayload(builder, payloadMember, response.Content, bodyFormat);
        else if (binding.OutputBodyProjection is { } projection && response.Content.Length > 0)
            bodyFormat.ReadInto(projection, response.Content, builder);

        return (TOutput)binding.OutputSchema.BuildObject(builder);
    }

    public static TOutput DeserializeResponse<TInput, TOutput>(
        FunctionalOperationSchema<TInput, TOutput> operation,
        SmithyHttpResponse response,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return DeserializeResponse(RestOperationBinding.From(operation), response, bodyFormat);
    }

    private static void WritePayload<TInput>(
        SmithyHttpRequest request,
        IFunctionalMemberSchema<TInput> member,
        TInput input,
        IFunctionalRestBodyFormat bodyFormat
    ) => member.Accept(new WritePayloadVisitor<TInput>(request, input, bodyFormat));

    private static void ReadPayload<TContainer>(
        object builder,
        IFunctionalMemberSchema<TContainer> member,
        byte[]? content,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        if (content is null or { Length: 0 })
        {
            if (TryCreateDefaultValue(member.Target, member.Traits, out var defaultValue))
            {
                member.SetObject(builder, defaultValue);
            }

            return;
        }

        member.Accept(new ReadPayloadVisitor<TContainer>(builder, content, bodyFormat));
    }

    private static byte[] SerializePayload<TOutput>(
        IFunctionalMemberSchema<TOutput> member,
        TOutput output,
        Dictionary<string, IReadOnlyList<string>> contentHeaders,
        IFunctionalRestBodyFormat bodyFormat
    ) => new SerializePayloadVisitor<TOutput>(output, contentHeaders, bodyFormat).Serialize(member);

    private static byte[] SerializeBody<T>(
        FunctionalSchema<T> schema,
        T value,
        Dictionary<string, IReadOnlyList<string>> contentHeaders,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        contentHeaders["Content-Type"] = [bodyFormat.ContentType];
        return bodyFormat.Serialize(schema, value);
    }

    private static byte[] SerializeProjectionBody<T>(
        FunctionalStructProjection<T> projection,
        T value,
        Dictionary<string, IReadOnlyList<string>> contentHeaders,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        contentHeaders["Content-Type"] = [bodyFormat.ContentType];
        return bodyFormat.Serialize(projection, value);
    }

    private static void WriteBody<T>(
        SmithyHttpRequest request,
        FunctionalSchema<T> schema,
        T value,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        request.Content = bodyFormat.Serialize(schema, value);
        request.ContentType = bodyFormat.ContentType;
    }

    private static void WriteProjectionBody<T>(
        SmithyHttpRequest request,
        FunctionalStructProjection<T> projection,
        T value,
        IFunctionalRestBodyFormat bodyFormat
    )
    {
        request.Content = bodyFormat.Serialize(projection, value);
        request.ContentType = bodyFormat.ContentType;
    }

    private sealed class WritePayloadVisitor<TInput>(
        SmithyHttpRequest request,
        TInput input,
        IFunctionalRestBodyFormat bodyFormat
    ) : IFunctionalMemberVisitor<TInput>
    {
        public void Visit<TValue>(IFunctionalMemberSchema<TInput, TValue> member)
        {
            var value = member.GetValue(input);
            if (value is null)
            {
                return;
            }

            if (IsDefaultValue(member.TargetSchema, member.Traits, value))
            {
                return;
            }

            if (member.TargetSchema.Kind == ShapeKind.Blob)
            {
                request.Content = (byte[])(object)value;
                request.ContentType = bodyFormat.BlobContentType;
                return;
            }

            WriteBody(request, member.TargetSchema, value, bodyFormat);
        }
    }

    private sealed class ReadPayloadVisitor<TContainer>(
        object builder,
        byte[] content,
        IFunctionalRestBodyFormat bodyFormat
    ) : IFunctionalMemberVisitor<TContainer>
    {
        public void Visit<TValue>(IFunctionalMemberSchema<TContainer, TValue> member)
        {
            if (member.TargetSchema.Kind == ShapeKind.Blob)
            {
                member.SetObject(builder, content);
                return;
            }

            member.SetObject(builder, bodyFormat.Deserialize(member.TargetSchema, content));
        }
    }

    private sealed class SerializePayloadVisitor<TOutput>(
        TOutput output,
        Dictionary<string, IReadOnlyList<string>> contentHeaders,
        IFunctionalRestBodyFormat bodyFormat
    ) : IFunctionalMemberVisitor<TOutput>
    {
        private byte[] content = [];

        public byte[] Serialize(IFunctionalMemberSchema<TOutput> member)
        {
            member.Accept(this);
            return content;
        }

        public void Visit<TValue>(IFunctionalMemberSchema<TOutput, TValue> member)
        {
            var value = member.GetValue(output);
            if (value is null)
            {
                return;
            }

            if (member.TargetSchema.Kind == ShapeKind.Blob)
            {
                contentHeaders["Content-Type"] = [bodyFormat.BlobContentType];
                content = (byte[])(object)value;
                return;
            }

            content = SerializeBody(member.TargetSchema, value, contentHeaders, bodyFormat);
        }
    }

    private static string BuildRequestUri<TInput>(
        string uriTemplate,
        IReadOnlyList<IFunctionalMemberSchema> labelMembers,
        TInput input
    )
    {
        var requestUri = uriTemplate;
        foreach (var member in labelMembers)
        {
            var value = member.GetObject(input!);
            if (value is null)
                throw new InvalidOperationException(
                    $"HTTP label member '{member.Name}' cannot be null."
                );

            requestUri = requestUri
                .Replace(
                    "{" + member.Name + "+}",
                    EscapeGreedyLabel(member.Target, member.Traits, value),
                    StringComparison.Ordinal
                )
                .Replace(
                    "{" + member.Name + "}",
                    Uri.EscapeDataString(FormatHttpValue(member.Target, member.Traits, value)),
                    StringComparison.Ordinal
                );
        }

        return requestUri;
    }

    private static void AddHeader<TInput>(
        IDictionary<string, IReadOnlyList<string>> headers,
        string name,
        IFunctionalMemberSchema member,
        TInput input
    )
    {
        var value = member.GetObject(input!);
        if (value is null)
        {
            return;
        }

        headers[name] = [FormatHttpHeaderValue(member, value)];
    }

    private static void AddPrefixedHeaders<TInput>(
        IDictionary<string, IReadOnlyList<string>> headers,
        string prefix,
        IFunctionalMemberSchema member,
        TInput input
    )
    {
        var value = member.GetObject(input!);
        if (value is null)
        {
            return;
        }

        var mapSchema = RequireMap(member);
        foreach (var entry in mapSchema.GetEntriesObject(value))
        {
            if (entry.Value is null)
            {
                continue;
            }

            headers[$"{prefix}{entry.Key}"] = [FormatHttpValue(mapSchema.Value, entry.Value)];
        }
    }

    private static object ReadPrefixedHeaders(
        IFunctionalMemberSchema member,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> headers,
        string prefix
    )
    {
        var mapSchema = RequireMap(member);
        var builder = mapSchema.CreateBuilder();
        foreach (var header in headers)
        {
            if (
                !header.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || header.Value.Count == 0
            )
            {
                continue;
            }

            mapSchema.AddObject(
                builder,
                header.Key[prefix.Length..],
                ParseHttpValue(mapSchema.Value, header.Value[0])
            );
        }

        return mapSchema.BuildObject(builder);
    }

    private static object ReadQueryParams(
        IFunctionalMemberSchema member,
        Dictionary<string, IReadOnlyList<string>> query,
        HashSet<string> excludedNames
    )
    {
        var mapSchema = RequireMap(member);
        var builder = mapSchema.CreateBuilder();
        foreach (var entry in query)
        {
            if (entry.Value.Count == 0 || excludedNames.Contains(entry.Key))
            {
                continue;
            }

            mapSchema.AddObject(
                builder,
                entry.Key,
                ParseHttpValue(mapSchema.Value, entry.Value[0])
            );
        }

        return mapSchema.BuildObject(builder);
    }

    private static string AppendQuery<TInput>(
        string requestUri,
        string name,
        IFunctionalMemberSchema member,
        TInput input
    )
    {
        var value = member.GetObject(input!);
        if (value is null)
        {
            return requestUri;
        }

        var builder = new StringBuilder(requestUri);
        AppendQueryValue(builder, name, member.Target, member.Traits, value);
        return builder.ToString();
    }

    private static string AppendQueryParams<TInput>(
        string requestUri,
        IFunctionalMemberSchema member,
        TInput input
    )
    {
        var value = member.GetObject(input!);
        if (value is null)
        {
            return requestUri;
        }

        var builder = new StringBuilder(requestUri);
        var mapSchema = RequireMap(member);
        foreach (var entry in mapSchema.GetEntriesObject(value))
        {
            if (entry.Value is null)
            {
                continue;
            }

            AppendQueryValue(builder, entry.Key, mapSchema.Value, entry.Value);
        }

        return builder.ToString();
    }

    private static IFunctionalMapSchema RequireMap(IFunctionalMemberSchema member) =>
        member.Target.Resolved is IFunctionalMapSchema mapSchema
            ? mapSchema
            : throw new InvalidOperationException(
                $"HTTP binding member '{member.Name}' must target a map schema."
            );

    private static void AppendQueryValue(
        StringBuilder builder,
        string name,
        FunctionalSchema schema,
        object value
    ) => AppendQueryValue(builder, name, schema, traits: null, value);

    private static void AppendQueryValue(
        StringBuilder builder,
        string name,
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object value
    )
    {
        if (schema.Resolved is IFunctionalListSchema listSchema)
        {
            foreach (var element in listSchema.GetElementsObject(value))
            {
                if (element is not null)
                {
                    AppendPrimitiveQueryValue(builder, name, listSchema.Element, traits, element);
                }
            }

            return;
        }

        AppendPrimitiveQueryValue(builder, name, schema, traits, value);
    }

    private static void AppendPrimitiveQueryValue(
        StringBuilder builder,
        string name,
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object value
    )
    {
        builder.Append(builder.ToString().Contains('?') ? '&' : '?');
        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(FormatHttpValue(schema, traits, value)));
    }

    private static string FormatHttpHeaderValue(IFunctionalMemberSchema member, object value)
    {
        if (member.Target.Resolved is IFunctionalListSchema listSchema)
        {
            return string.Join(
                ", ",
                listSchema
                    .GetElementsObject(value)
                    .Where(element => element is not null)
                    .Select(element =>
                        FormatHttpHeaderListValue(listSchema.Element, member.Traits, element!)
                    )
            );
        }

        return FormatHttpValue(member.Target, member.Traits, value);
    }

    private static string FormatHttpValue(FunctionalSchema schema, object value)
    {
        return FormatHttpValue(schema, traits: null, value);
    }

    private static string FormatHttpValue(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object value
    )
    {
        schema = UnwrapNullable(schema);
        return schema.Kind switch
        {
            ShapeKind.Boolean => ((bool)value).ToString().ToLowerInvariant(),
            ShapeKind.Byte => ((sbyte)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Short => ((short)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Integer => ((int)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Long => ((long)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Float => FormatFloat((float)value),
            ShapeKind.Double => FormatDouble((double)value),
            ShapeKind.BigInteger => ((BigInteger)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.BigDecimal => ((decimal)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.String => HasTrait(schema, traits, FunctionalRestTraits.MediaType)
                ? Convert.ToBase64String(Encoding.UTF8.GetBytes((string)value))
                : (string)value,
            ShapeKind.Enum => ((IFunctionalStringEnumValue)value).Value,
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)schema)
                .GetIntegerValueObject(value)
                .ToString(CultureInfo.InvariantCulture),
            ShapeKind.Blob => Convert.ToBase64String((byte[])value),
            ShapeKind.Timestamp => FormatTimestamp(schema, traits, (DateTimeOffset)value),
            _ => throw new NotSupportedException(
                $"RestJson HTTP binding codec does not support schema kind '{schema.Kind}'."
            ),
        };
    }

    private static string FormatHttpHeaderListValue(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object value
    )
    {
        schema = UnwrapNullable(schema);
        var formatted = FormatHttpValue(schema, traits, value);
        if (schema.Kind is not (ShapeKind.String or ShapeKind.Enum))
        {
            return formatted;
        }

        return NeedsQuotedHeaderValue(formatted) ? QuoteHeaderValue(formatted) : formatted;
    }

    private static object? ParseHttpBindingValue(FunctionalSchema schema, string value)
    {
        return ParseHttpBindingValue(schema, traits: null, value);
    }

    private static object? ParseHttpBindingValue(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        if (schema is IFunctionalListSchema listSchema)
        {
            var element = UnwrapNullable(listSchema.Element);
            return ParseHttpBindingValues(
                schema,
                traits,
                SplitHeaderList(value, element.Kind).ToArray()
            );
        }

        return ParseHttpValue(schema, traits, value);
    }

    private static object? ParseHttpBindingValues(
        FunctionalSchema schema,
        IReadOnlyList<string> values
    ) => ParseHttpBindingValues(schema, traits: null, values);

    private static object? ParseHttpBindingValues(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        IReadOnlyList<string> values
    )
    {
        if (schema is not IFunctionalListSchema listSchema)
        {
            return values.Count > 0 ? ParseHttpValue(schema, traits, values[0]) : null;
        }

        var builder = listSchema.CreateBuilder();
        foreach (var value in values)
        {
            listSchema.AddObject(
                builder,
                ParseHttpValue(UnwrapNullable(listSchema.Element), traits, value)
            );
        }

        return listSchema.BuildObject(builder);
    }

    private static object? ParseHttpValue(FunctionalSchema schema, string value)
    {
        return ParseHttpValue(schema, traits: null, value);
    }

    private static object? ParseHttpValue(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        schema = UnwrapNullable(schema);
        return schema.Kind switch
        {
            ShapeKind.Boolean => bool.Parse(value),
            ShapeKind.Byte => sbyte.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Short => short.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Integer => int.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Long => long.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.Float => ParseFloat(value),
            ShapeKind.Double => ParseDouble(value),
            ShapeKind.BigInteger => BigInteger.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.BigDecimal => decimal.Parse(value, CultureInfo.InvariantCulture),
            ShapeKind.String => HasTrait(schema, traits, FunctionalRestTraits.MediaType)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(value))
                : value,
            ShapeKind.Enum => ((IFunctionalStringEnumSchema)schema).CreateObject(value),
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)schema).CreateObject(
                int.Parse(value, CultureInfo.InvariantCulture)
            ),
            ShapeKind.Blob => Convert.FromBase64String(value),
            ShapeKind.Timestamp => ParseTimestamp(schema, traits, value),
            _ => throw new NotSupportedException(
                $"RestJson HTTP binding codec does not support schema kind '{schema.Kind}'."
            ),
        };
    }

    private static FunctionalSchema UnwrapNullable(FunctionalSchema schema)
    {
        var resolved = schema.Resolved;
        return resolved is IFunctionalNullableSchema nullable ? nullable.Target.Resolved : resolved;
    }

    private static bool IsDefaultValue(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        object value
    )
    {
        if (!TryCreateDefaultValue(schema, traits, out var defaultValue))
        {
            return false;
        }

        return value is byte[] bytes && defaultValue is byte[] defaultBytes
            ? bytes.SequenceEqual(defaultBytes)
            : EqualityComparer<object>.Default.Equals(value, defaultValue);
    }

    private static bool TryCreateDefaultValue(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        out object? value
    )
    {
        if (!traits.TryGetValue(DefaultTrait, out var trait))
        {
            value = null;
            return false;
        }

        value = CreateDefaultValue(UnwrapNullable(schema), trait.Value);
        return true;
    }

    private static object? CreateDefaultValue(FunctionalSchema schema, Document value)
    {
        return schema.Kind switch
        {
            ShapeKind.Boolean => value.AsBoolean(),
            ShapeKind.Byte => (sbyte)value.AsNumber(),
            ShapeKind.Short => (short)value.AsNumber(),
            ShapeKind.Integer => (int)value.AsNumber(),
            ShapeKind.Long => (long)value.AsNumber(),
            ShapeKind.Float => (float)value.AsNumber(),
            ShapeKind.Double => (double)value.AsNumber(),
            ShapeKind.BigInteger => new BigInteger(value.AsNumber()),
            ShapeKind.BigDecimal => value.AsNumber(),
            ShapeKind.String => value.AsString(),
            ShapeKind.Enum => ((IFunctionalStringEnumSchema)schema).CreateObject(value.AsString()),
            ShapeKind.IntEnum => ((IFunctionalIntEnumSchema)schema).CreateObject(
                (int)value.AsNumber()
            ),
            ShapeKind.Blob => Convert.FromBase64String(value.AsString()),
            _ => null,
        };
    }

    private static string EscapeGreedyLabel(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object value
    )
    {
        return string.Join(
            "/",
            FormatHttpValue(schema, traits, value).Split('/').Select(Uri.EscapeDataString)
        );
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value))
        {
            return "NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static float ParseFloat(string value) =>
        value switch
        {
            "NaN" => float.NaN,
            "Infinity" => float.PositiveInfinity,
            "-Infinity" => float.NegativeInfinity,
            _ => float.Parse(value, CultureInfo.InvariantCulture),
        };

    private static double ParseDouble(string value) =>
        value switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => double.Parse(value, CultureInfo.InvariantCulture),
        };

    private static string FormatTimestamp(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        DateTimeOffset value
    )
    {
        return GetTimestampFormat(schema, traits) switch
        {
            "epoch-seconds" => FormatEpochSeconds(value),
            "http-date" => value
                .ToUniversalTime()
                .ToString("ddd, dd MMM yyyy HH':'mm':'ss 'GMT'", CultureInfo.InvariantCulture),
            "date-time" => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            var format => throw new NotSupportedException(
                $"Timestamp format '{format}' is not supported."
            ),
        };
    }

    private static DateTimeOffset ParseTimestamp(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        return GetTimestampFormat(schema, traits) switch
        {
            "epoch-seconds" => ParseEpochSeconds(value),
            "http-date" => DateTimeOffset.ParseExact(value, "r", CultureInfo.InvariantCulture),
            "date-time" => DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
            var format => throw new NotSupportedException(
                $"Timestamp format '{format}' is not supported."
            ),
        };
    }

    private static string GetTimestampFormat(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits
    )
    {
        if (TryGetTrait(schema, traits, FunctionalRestTraits.TimestampFormat, out var trait))
        {
            return trait.Value.AsString();
        }

        if (HasTrait(schema, traits, FunctionalRestTraits.HttpHeader))
        {
            return "http-date";
        }

        return "date-time";
    }

    private static DateTimeOffset ParseEpochSeconds(string value)
    {
        var seconds = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        var wholeSeconds = decimal.Truncate(seconds);
        var fractionalSeconds = seconds - wholeSeconds;
        return DateTimeOffset
            .FromUnixTimeSeconds((long)wholeSeconds)
            .AddTicks((long)(fractionalSeconds * TimeSpan.TicksPerSecond));
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

    private static bool HasTrait(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        ShapeId id
    ) => TryGetTrait(schema, traits, id, out _);

    private static bool TryGetTrait(
        FunctionalSchema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        ShapeId id,
        out Trait trait
    )
    {
        if (traits?.TryGetValue(id, out trait) == true)
        {
            return true;
        }

        if (schema.Traits.TryGetValue(id, out trait))
        {
            return true;
        }

        trait = default;
        return false;
    }

    private static IEnumerable<string> SplitHeaderList(string value, ShapeKind elementKind)
    {
        if (
            elementKind == ShapeKind.Timestamp
            && value.Contains("GMT,", StringComparison.OrdinalIgnoreCase)
        )
        {
            var segments = value.Split("GMT,", StringSplitOptions.None);
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i].Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                yield return i < segments.Length - 1 ? segment + " GMT" : segment;
            }

            yield break;
        }

        if (
            elementKind is ShapeKind.String or ShapeKind.Enum
            && value.Contains('"', StringComparison.Ordinal)
        )
        {
            foreach (var part in ParseQuotedHeaderList(value))
            {
                yield return part;
            }

            yield break;
        }

        foreach (var part in value.Split(','))
        {
            yield return part.Trim();
        }
    }

    private static IEnumerable<string> ParseQuotedHeaderList(string value)
    {
        var builder = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        foreach (var ch in value)
        {
            if (escaping)
            {
                builder.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\' && inQuotes)
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                yield return builder.ToString().Trim();
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0 || (value.Length > 0 && value[^1] == ','))
        {
            yield return builder.ToString().Trim();
        }
    }

    private static bool NeedsQuotedHeaderValue(string value) =>
        value.Length == 0
        || value.Any(ch => ch == ',' || ch == '"' || ch == '\\' || char.IsWhiteSpace(ch));

    private static string QuoteHeaderValue(string value) =>
        "\""
        + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
        + "\"";

    private static Dictionary<string, string> ExtractLabels(string pattern, string requestUri)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = requestUri.Split('?', 2)[0];
        var patternSegments = pattern.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < patternSegments.Length && i < pathSegments.Length; i++)
        {
            var patternSegment = patternSegments[i];
            if (
                patternSegment.Length > 3
                && patternSegment[0] == '{'
                && patternSegment[^2] == '+'
                && patternSegment[^1] == '}'
            )
            {
                labels[patternSegment[1..^2]] = Uri.UnescapeDataString(
                    string.Join("/", pathSegments.Skip(i))
                );
                break;
            }

            if (patternSegment.Length > 2 && patternSegment[0] == '{' && patternSegment[^1] == '}')
            {
                labels[patternSegment[1..^1]] = Uri.UnescapeDataString(pathSegments[i]);
            }
        }

        return labels;
    }

    private static Dictionary<string, IReadOnlyList<string>> ParseQuery(string requestUri)
    {
        var queryStart = requestUri.IndexOf('?', StringComparison.Ordinal);
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (queryStart < 0 || queryStart == requestUri.Length - 1)
        {
            return values.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value,
                StringComparer.Ordinal
            );
        }

        foreach (var pair in requestUri[(queryStart + 1)..].Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            var parts = pair.Split('=', 2);
            var name = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (!values.TryGetValue(name, out var existing))
            {
                existing = [];
                values[name] = existing;
            }

            existing.Add(value);
        }

        return values.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value,
            StringComparer.Ordinal
        );
    }

    private static bool TryGetFirstHeader(
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> headers,
        string name,
        out string value
    )
    {
        foreach (var header in headers)
        {
            if (
                string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
            )
            {
                value = header.Value[0];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
