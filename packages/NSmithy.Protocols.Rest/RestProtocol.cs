using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.Rest;

/// <summary>
/// Supplies the body wire format for a REST protocol: the content types it emits and a factory for
/// building body codecs (JSON for restJson1 / simpleRestJson, XML for restXml). Codecs are compiled
/// once per operation when the <see cref="RestOperationBinding{TInput, TOutput}"/> is built, never
/// per call.
/// </summary>
public interface IRestBodyCodecFactory
{
    string ContentType { get; }

    string BlobContentType { get; }

    ICodec<T> CodecFor<T>(Schema<T> schema);

    IProjectionCodec<T> CodecFor<T>(
        StructProjection<T> projection,
        bool materializeTopLevelDefaults
    );
}

/// <summary>A serialized REST body: the encoded bytes plus the Content-Type to advertise.</summary>
public readonly record struct RestBody(byte[] Content, string? ContentType)
{
    /// <summary>No body — the member was null/absent, so neither content nor Content-Type is written.</summary>
    public static RestBody None { get; } = new([], null);

    public bool HasContent => Content.Length > 0 || ContentType is not null;
}

public static class RestProtocol
{
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        RestOperationBinding<TInput, TOutput> binding,
        TInput input
    )
    {
        ArgumentNullException.ThrowIfNull(binding);

        var requestUri = BuildRequestUri(binding.UriTemplate, binding.LabelMembers, input!);

        foreach (var (member, queryName) in binding.QueryMembers)
            requestUri = AppendQuery(requestUri, queryName, member, input!);
        if (binding.QueryParamsMember is { } qpMember)
            requestUri = AppendQueryParams(requestUri, qpMember, input!, binding.BoundQueryNames);

        var request = new SmithyHttpRequest(binding.HttpMethod, requestUri);
        request.Headers["Accept"] = [binding.AcceptType];

        foreach (var (member, headerName) in binding.RequestHeaderMembers)
            AddRequestHeader(request, headerName, member, input!);
        if (binding.RequestPrefixHeadersMember is { } phMember)
            AddPrefixedHeaders(request.Headers, phMember.Prefix, phMember.Member, input!);

        if (binding.InputPayloadWriter is { } writePayload)
        {
            var body = writePayload(input!);
            if (body.HasContent)
            {
                request.Content = body.Content;
                if (body.ContentType is not null)
                    SetContentTypeIfMissing(request, body.ContentType);
            }
            return request;
        }
        if (binding.InputBodyCodec is { } codec)
        {
            request.Content = codec.Serialize(input!);
            SetContentTypeIfMissing(request, binding.BodyContentType);
        }

        return request;
    }

    public static TInput DeserializeRequest<TInput, TOutput>(
        RestOperationBinding<TInput, TOutput> binding,
        SmithyHttpRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);

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
        if (binding.InputPayloadReader is { } readPayload)
            readPayload(request.Content, builder);
        else if (binding.InputBodyCodec is { } codec && request.Content is { Length: > 0 } content)
            codec.ReadInto(content, builder);

        return (TInput)binding.InputSchema.BuildObject(builder);
    }

    public static SmithyHttpResponse SerializeResponse<TInput, TOutput>(
        RestOperationBinding<TInput, TOutput> binding,
        TOutput output
    )
    {
        ArgumentNullException.ThrowIfNull(binding);

        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var statusCode = (HttpStatusCode)binding.SuccessStatusCode;

        if (binding.ResponseCodeMember is { } codeMember)
        {
            var value = codeMember.GetObject(output!);
            if (value is not null)
                statusCode = (HttpStatusCode)
                    (int)ParseHttpValue(Schemas.Integer, value.ToString()!)!;
        }
        foreach (var (member, headerName) in binding.ResponseHeaderMembers)
            AddHeader(headers, headerName, member, output!);
        if (binding.ResponsePrefixHeadersMember is { } respPhMember)
            AddPrefixedHeaders(headers, respPhMember.Prefix, respPhMember.Member, output!);

        byte[] content = [];
        if (binding.OutputPayloadWriter is { } writePayload)
        {
            var body = writePayload(output!);
            content = body.Content;
            if (body.ContentType is not null)
                contentHeaders["Content-Type"] = [body.ContentType];
        }
        else if (binding.OutputBodyCodec is { } codec)
        {
            content = codec.Serialize(output!);
            contentHeaders["Content-Type"] = [binding.BodyContentType];
        }

        return new SmithyHttpResponse(statusCode, null, content, headers, contentHeaders);
    }

    public static TOutput DeserializeResponse<TInput, TOutput>(
        RestOperationBinding<TInput, TOutput> binding,
        SmithyHttpResponse response
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(response);

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
        if (binding.OutputPayloadReader is { } readPayload)
            readPayload(response.Content, builder);
        else if (binding.OutputBodyCodec is { } codec && response.Content.Length > 0)
            codec.ReadInto(response.Content, builder);

        return (TOutput)binding.OutputSchema.BuildObject(builder);
    }

    /// <summary>
    /// Deserializes an error structure (the same HTTP binding rules as an output) from a
    /// response. Error members are partitioned per call; errors are off the hot path.
    /// </summary>
    public static TError DeserializeError<TError>(
        Schema<TError> errorSchema,
        SmithyHttpResponse response,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(codecFactory);

        if (errorSchema.Resolved is not IStructSchema<TError> schema)
        {
            throw new InvalidOperationException(
                $"Error schema '{errorSchema.Id}' must be a structure schema."
            );
        }

        var builder = schema.CreateBuilder();
        var bodyMembers = new List<IMemberSchema<TError>>();

        foreach (var member in schema.TypedMembers)
        {
            if (member.Traits.ContainsKey(RestTraits.HttpResponseCode))
            {
                member.SetObject(
                    builder,
                    ParseHttpValue(
                        member.Target,
                        ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                    )
                );
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                var name = headerTrait.Value.AsString();
                if (
                    TryGetFirstHeader(response.Headers, name, out var header)
                    || TryGetFirstHeader(response.ContentHeaders, name, out header)
                )
                {
                    member.SetObject(
                        builder,
                        ParseHttpBindingValue(member.Target, member.Traits, header)
                    );
                }
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait))
            {
                member.SetObject(
                    builder,
                    ReadPrefixedHeaders(member, response.Headers, prefixTrait.Value.AsString())
                );
            }
            else if (member.Traits.ContainsKey(RestTraits.HttpPayload))
            {
                BuildPayloadReader(member, codecFactory, rawStringPayloads)(
                    response.Content,
                    builder
                );
            }
            else
            {
                bodyMembers.Add(member);
            }
        }

        if (bodyMembers.Count > 0 && response.Content.Length > 0)
        {
            var projection = Schemas.Project(schema, bodyMembers);
            codecFactory
                .CodecFor(projection, materializeTopLevelDefaults: true)
                .ReadInto(response.Content, builder);
        }

        return (TError)schema.BuildObject(builder);
    }

    /// <summary>
    /// Serializes a modeled error into a REST error response: the supplied HTTP status code, an
    /// <c>X-Amzn-Errortype</c> discriminator carrying the error's shape name, the error's HTTP
    /// header/payload bindings, and a body holding the remaining members (at minimum <c>{}</c>).
    /// </summary>
    public static SmithyHttpResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError value,
        string errorShapeId,
        int statusCode,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        string errorTypeHeader
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(errorShapeId);
        ArgumentNullException.ThrowIfNull(codecFactory);

        if (errorSchema.Resolved is not IStructSchema<TError> schema)
        {
            throw new InvalidOperationException(
                $"Error schema '{errorSchema.Id}' must be a structure schema."
            );
        }

        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            [errorTypeHeader] = [LocalName(errorShapeId)],
        };
        var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );

        IMemberSchema<TError>? payloadMember = null;
        var bodyMembers = new List<IMemberSchema<TError>>();
        foreach (var member in schema.TypedMembers)
        {
            if (member.Traits.ContainsKey(RestTraits.HttpResponseCode))
            {
                continue;
            }

            if (member.Traits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                AddHeader(headers, headerTrait.Value.AsString(), member, value);
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait))
            {
                AddPrefixedHeaders(headers, prefixTrait.Value.AsString(), member, value);
            }
            else if (member.Traits.ContainsKey(RestTraits.HttpPayload))
            {
                payloadMember = member;
            }
            else
            {
                bodyMembers.Add(member);
            }
        }

        byte[] content;
        if (payloadMember is not null)
        {
            var body = BuildPayloadWriter(
                payloadMember,
                codecFactory,
                rawStringPayloads,
                emptyStructOnNull: false
            )(value);
            content = body.Content;
            if (body.ContentType is not null)
                contentHeaders["Content-Type"] = [body.ContentType];
        }
        else
        {
            content = codecFactory
                .CodecFor(Schemas.Project(schema, bodyMembers), materializeTopLevelDefaults: true)
                .Serialize(value);
            contentHeaders["Content-Type"] = [codecFactory.ContentType];
        }

        return new SmithyHttpResponse(
            (HttpStatusCode)statusCode,
            null,
            content,
            headers,
            contentHeaders
        );
    }

    private static string LocalName(string shapeId)
    {
        var hash = shapeId.LastIndexOf('#');
        return hash >= 0 ? shapeId[(hash + 1)..] : shapeId;
    }

    private static void SetContentTypeIfMissing(SmithyHttpRequest request, string contentType)
    {
        if (request.ContentType is null)
        {
            request.ContentType = contentType;
        }
    }

    /// <summary>
    /// Content-Type advertised for an <c>@httpPayload</c> output; used to precompute the request
    /// Accept header. Schema-kind based, so no value is needed.
    /// </summary>
    internal static string PayloadContentType(
        Schema target,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    )
    {
        if (GetMediaType(target, traits) is { } mediaType)
        {
            return mediaType;
        }

        return UnwrapNullable(target).Kind switch
        {
            ShapeKind.Blob => codecFactory.BlobContentType,
            ShapeKind.String
            or ShapeKind.Enum when !UseBodyCodecForPayload(target, traits, rawStringPayloads) =>
                "text/plain",
            _ => codecFactory.ContentType,
        };
    }

    /// <summary>
    /// Builds — once, at binding construction — a delegate that serializes an <c>@httpPayload</c>
    /// member to its wire body. The blob/text/codec decision and the compiled body codec are baked
    /// in, so nothing is recompiled per request. <paramref name="emptyStructOnNull"/> separates the
    /// request side (a null struct payload still emits <c>{}</c>) from the response side (emits
    /// nothing).
    /// </summary>
    internal static Func<TContainer, RestBody> BuildPayloadWriter<TContainer>(
        IMemberSchema<TContainer> member,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool emptyStructOnNull
    )
    {
        var builder = new PayloadWriterBuilder<TContainer>(
            codecFactory,
            rawStringPayloads,
            emptyStructOnNull
        );
        member.Accept(builder);
        return builder.Result!;
    }

    /// <summary>Builds — once — a delegate that reads an <c>@httpPayload</c> member from the body.</summary>
    internal static Action<byte[]?, object> BuildPayloadReader<TContainer>(
        IMemberSchema<TContainer> member,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    )
    {
        var builder = new PayloadReaderBuilder<TContainer>(codecFactory, rawStringPayloads);
        member.Accept(builder);
        return builder.Result!;
    }

    private sealed class PayloadWriterBuilder<TContainer>(
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool emptyStructOnNull
    ) : IMemberVisitor<TContainer>
    {
        public Func<TContainer, RestBody>? Result { get; private set; }

        public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
        {
            var target = member.TargetSchema;
            var traits = member.Traits;
            var mediaType = GetMediaType(target, traits);
            var kind = UnwrapNullable(target).Kind;

            if (kind == ShapeKind.Blob)
            {
                var contentType = mediaType ?? codecFactory.BlobContentType;
                Result = container =>
                    member.GetValue(container) is { } value
                        ? new RestBody((byte[])(object)value, contentType)
                        : RestBody.None;
                return;
            }

            if (
                (kind == ShapeKind.String || kind == ShapeKind.Enum)
                && (
                    mediaType is not null
                    || !UseBodyCodecForPayload(target, traits, rawStringPayloads)
                )
            )
            {
                var contentType = mediaType ?? "text/plain";
                Result = container =>
                {
                    var value = member.GetValue(container);
                    if (value is null)
                        return RestBody.None;
                    var text =
                        kind == ShapeKind.Enum
                            ? ((IStringEnumValue)(object)value).Value
                            : (string)(object)value;
                    return new RestBody(Encoding.UTF8.GetBytes(text), contentType);
                };
                return;
            }

            // Codec path: structures, unions, documents, and string/enum that go through the body
            // codec (alloy simpleRestJson, or members carrying an explicit @default). The codec is
            // compiled once here; the empty-struct value for a null request payload is built lazily
            // (only if a null actually arrives) so binding construction never materializes a struct
            // with required members.
            var codec = codecFactory.CodecFor(target);
            var jsonContentType = codecFactory.ContentType;
            var writeEmptyStructOnNull = emptyStructOnNull;

            Result = container =>
            {
                var value = member.GetValue(container);
                if (value is null)
                {
                    return
                        writeEmptyStructOnNull
                        && TryCreateEmptyStructureValue(target, out _, out var emptyObj)
                        ? new RestBody(codec.Serialize((TValue)emptyObj!), jsonContentType)
                        : RestBody.None;
                }
                if (IsDefaultValue(target, traits, value))
                    return RestBody.None;
                return new RestBody(codec.Serialize(value), jsonContentType);
            };
        }
    }

    private sealed class PayloadReaderBuilder<TContainer>(
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    ) : IMemberVisitor<TContainer>
    {
        public Action<byte[]?, object>? Result { get; private set; }

        public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
        {
            var target = member.TargetSchema;
            var traits = member.Traits;
            var unwrapped = UnwrapNullable(target);

            if (unwrapped.Kind == ShapeKind.Blob)
            {
                Result = (content, builder) =>
                {
                    if (content is null or { Length: 0 })
                        ApplyDefault(member, target, traits, builder);
                    else
                        member.SetObject(builder, content);
                };
                return;
            }

            if (!UseBodyCodecForPayload(target, traits, rawStringPayloads))
            {
                Result = (content, builder) =>
                {
                    if (content is null or { Length: 0 })
                    {
                        ApplyDefault(member, target, traits, builder);
                        return;
                    }
                    var text = Encoding.UTF8.GetString(content);
                    member.SetObject(
                        builder,
                        unwrapped.Kind == ShapeKind.Enum
                            ? ((IStringEnumSchema)unwrapped).CreateObject(text)
                            : text
                    );
                };
                return;
            }

            var codec = codecFactory.CodecFor(target);
            Result = (content, builder) =>
            {
                if (content is null or { Length: 0 })
                    ApplyDefault(member, target, traits, builder);
                else
                    member.SetObject(builder, codec.Deserialize(content));
            };
        }

        private static void ApplyDefault(
            IMemberSchema member,
            Schema target,
            IReadOnlyDictionary<ShapeId, Trait> traits,
            object builder
        )
        {
            if (TryCreateDefaultValue(target, traits, out var defaultValue))
                member.SetObject(builder, defaultValue);
        }
    }

    private static string BuildRequestUri<TInput>(
        string uriTemplate,
        IReadOnlyList<IMemberSchema> labelMembers,
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
        Dictionary<string, IReadOnlyList<string>> headers,
        string name,
        IMemberSchema member,
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

    private static void AddRequestHeader<TInput>(
        SmithyHttpRequest request,
        string name,
        IMemberSchema member,
        TInput input
    )
    {
        var value = member.GetObject(input!);
        if (value is null)
        {
            return;
        }

        var formatted = FormatHttpHeaderValue(member, value);
        if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            request.ContentType = formatted;
            return;
        }

        if (string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase))
        {
            request.ContentHeaders[name] = [formatted];
            return;
        }

        request.Headers[name] = [formatted];
    }

    private static void AddPrefixedHeaders<TInput>(
        IDictionary<string, IReadOnlyList<string>> headers,
        string prefix,
        IMemberSchema member,
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

            var headerName = $"{prefix}{entry.Key}";
            if (!headers.ContainsKey(headerName))
            {
                headers[headerName] = [FormatHttpValue(mapSchema.Value, entry.Value)];
            }
        }
    }

    private static object ReadPrefixedHeaders(
        IMemberSchema member,
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
        IMemberSchema member,
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
                ParseHttpBindingValues(mapSchema.Value, entry.Value)
            );
        }

        return mapSchema.BuildObject(builder);
    }

    private static string AppendQuery<TInput>(
        string requestUri,
        string name,
        IMemberSchema member,
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
        IMemberSchema member,
        TInput input,
        HashSet<string> excludedNames
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
            if (entry.Value is null || excludedNames.Contains(entry.Key))
            {
                continue;
            }

            AppendQueryValue(builder, entry.Key, mapSchema.Value, entry.Value);
        }

        return builder.ToString();
    }

    private static IMapSchema RequireMap(IMemberSchema member) =>
        member.Target.Resolved is IMapSchema mapSchema
            ? mapSchema
            : throw new InvalidOperationException(
                $"HTTP binding member '{member.Name}' must target a map schema."
            );

    private static void AppendQueryValue(
        StringBuilder builder,
        string name,
        Schema schema,
        object value
    ) => AppendQueryValue(builder, name, schema, traits: null, value);

    private static void AppendQueryValue(
        StringBuilder builder,
        string name,
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object value
    )
    {
        if (schema.Resolved is IListSchema listSchema)
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
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object value
    )
    {
        builder.Append(builder.ToString().Contains('?') ? '&' : '?');
        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(FormatHttpValue(schema, traits, value)));
    }

    private static string FormatHttpHeaderValue(IMemberSchema member, object value)
    {
        if (member.Target.Resolved is IListSchema listSchema)
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

    private static string FormatHttpValue(Schema schema, object value)
    {
        return FormatHttpValue(schema, traits: null, value);
    }

    private static string FormatHttpValue(
        Schema schema,
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
            ShapeKind.String => HasTrait(schema, traits, RestTraits.MediaType)
                ? Convert.ToBase64String(Encoding.UTF8.GetBytes((string)value))
                : (string)value,
            ShapeKind.Enum => ((IStringEnumValue)value).Value ?? string.Empty,
            ShapeKind.IntEnum => ((IIntEnumSchema)schema)
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
        Schema schema,
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

    private static object? ParseHttpBindingValue(Schema schema, string value)
    {
        return ParseHttpBindingValue(schema, traits: null, value);
    }

    private static object? ParseHttpBindingValue(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        if (schema.Resolved is IListSchema listSchema)
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

    private static object? ParseHttpBindingValues(Schema schema, IReadOnlyList<string> values) =>
        ParseHttpBindingValues(schema, traits: null, values);

    private static object? ParseHttpBindingValues(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        IReadOnlyList<string> values
    )
    {
        if (schema.Resolved is not IListSchema listSchema)
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

    private static object? ParseHttpValue(Schema schema, string value)
    {
        return ParseHttpValue(schema, traits: null, value);
    }

    private static object? ParseHttpValue(
        Schema schema,
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
            ShapeKind.String => HasTrait(schema, traits, RestTraits.MediaType)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(value))
                : value,
            ShapeKind.Enum => ((IStringEnumSchema)schema).CreateObject(value),
            ShapeKind.IntEnum => ((IIntEnumSchema)schema).CreateObject(
                int.Parse(value, CultureInfo.InvariantCulture)
            ),
            ShapeKind.Blob => Convert.FromBase64String(value),
            ShapeKind.Timestamp => ParseTimestamp(schema, traits, value),
            _ => throw new NotSupportedException(
                $"RestJson HTTP binding codec does not support schema kind '{schema.Kind}'."
            ),
        };
    }

    private static Schema UnwrapNullable(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }

    private static bool TryCreateEmptyStructureValue(
        Schema schema,
        out Schema structureSchema,
        out object value
    )
    {
        structureSchema = UnwrapNullable(schema);
        if (structureSchema.Resolved is IStructSchema structSchema)
        {
            value = structSchema.BuildObject(structSchema.CreateBuilder());
            return true;
        }

        value = null!;
        return false;
    }

    private static bool IsDefaultValue(
        Schema schema,
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

    private static bool UseBodyCodecForPayload(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        bool rawStringPayloads
    )
    {
        var kind = UnwrapNullable(schema).Kind;
        if (kind is not (ShapeKind.String or ShapeKind.Enum))
        {
            return true;
        }

        // String/enum payloads are raw text under restJson1/restXml; protocols that don't use raw
        // string payloads (alloy simpleRestJson) route them through the body codec, as do members
        // carrying an explicit @default.
        return traits.ContainsKey(DefaultTrait) || !rawStringPayloads;
    }

    private static bool TryCreateDefaultValue(
        Schema schema,
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

    private static object? CreateDefaultValue(Schema schema, Document value)
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
            ShapeKind.Enum => ((IStringEnumSchema)schema).CreateObject(value.AsString()),
            ShapeKind.IntEnum => ((IIntEnumSchema)schema).CreateObject((int)value.AsNumber()),
            ShapeKind.Blob => Convert.FromBase64String(value.AsString()),
            _ => null,
        };
    }

    private static string EscapeGreedyLabel(
        Schema schema,
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

    private static string FormatRfc3339(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(
        Schema schema,
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
            "date-time" => FormatRfc3339(value),
            var format => throw new NotSupportedException(
                $"Timestamp format '{format}' is not supported."
            ),
        };
    }

    private static DateTimeOffset ParseTimestamp(
        Schema schema,
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
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits
    )
    {
        if (TryGetTrait(schema, traits, RestTraits.TimestampFormat, out var trait))
        {
            return trait.Value.AsString();
        }

        if (HasTrait(schema, traits, RestTraits.HttpHeader))
        {
            return "http-date";
        }

        return "date-time";
    }

    private static string? GetMediaType(Schema schema, IReadOnlyDictionary<ShapeId, Trait> traits)
    {
        return TryGetTrait(schema, traits, RestTraits.MediaType, out var trait)
            ? trait.Value.AsString()
            : null;
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
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        ShapeId id
    ) => TryGetTrait(schema, traits, id, out _);

    private static bool TryGetTrait(
        Schema schema,
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
        // The URI template may carry a constant query string (e.g. `/Foo/{id}?bar=baz`); only the
        // path portion contains label placeholders.
        var patternPath = pattern.Split('?', 2)[0];
        var patternSegments = patternPath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
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
