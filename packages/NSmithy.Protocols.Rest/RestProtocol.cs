using System.Globalization;
using System.Net;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.EventStream;
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

    ICodec<T> CodecFor<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait>? memberTraits = null
    );

    IProjectionCodec<T, TBuilder> CodecFor<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults,
        string? defaultRootName = null
    );

    byte[] PrepareErrorBody(byte[] content) => content;
}

public delegate void RestPayloadReader(byte[]? content, Stream? streamingContent, object builder);

/// <summary>A serialized REST body: buffered bytes or a stream plus the Content-Type to advertise.</summary>
/// <summary>
/// An error-response writer with every schema-derived decision already made. Produced by
/// <c>RestProtocol.CompileErrorSerializer</c> once per error shape and invoked per response.
/// </summary>
internal delegate SmithyHttpServerResponse RestErrorSerializer<in TError>(
    TError value,
    string errorShapeId,
    int statusCode
);

public readonly record struct RestBody(
    byte[] Content,
    string? ContentType,
    Stream? StreamingContent = null,
    long? StreamingContentLength = null,
    IAsyncEnumerable<ReadOnlyMemory<byte>>? EventStreamingContent = null
)
{
    /// <summary>No body — the member was null/absent, so neither content nor Content-Type is written.</summary>
    public static RestBody None { get; } = new([], null);

    public bool HasContent =>
        EventStreamingContent is not null
        || StreamingContent is not null
        || Content.Length > 0
        || ContentType is not null;
}

public static class RestProtocol
{
    internal const string EventStreamContentType = "application/vnd.amazon.eventstream";

    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    public static SmithyHttpRequest SerializeRequest<
        TInput,
        TOutput,
        TInputBuilder,
        TOutputBuilder
    >(RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> binding, TInput input)
        where TInputBuilder : notnull
        where TOutputBuilder : notnull
    {
        ArgumentNullException.ThrowIfNull(binding);

        var requestUri = BuildRequestUri(binding.UriTemplate, binding.LabelMembers, input!);

        foreach (var (member, queryName) in binding.QueryMembers)
            requestUri = AppendQuery(requestUri, queryName, member, input!);
        if (binding.QueryParamsMember is { } qpMember)
            requestUri = AppendQueryParams(requestUri, qpMember, input!, binding.BoundQueryNames);

        var request = new SmithyHttpRequest(binding.HttpMethod, requestUri);
        request.Headers["Accept"] = [binding.AcceptType];
        request.ExpectStreamingResponse = binding.OutputHasStreamingPayload;

        foreach (var (member, headerName) in binding.RequestHeaderMembers)
            AddRequestHeader(request, headerName, member, input!);
        if (binding.RequestPrefixHeadersMember is { } phMember)
            AddPrefixedHeaders(request.Headers, phMember.Prefix, phMember.Member, input!);

        if (binding.InputPayloadWriter is { } writePayload)
        {
            var body = writePayload(input!);
            if (body.HasContent)
            {
                request.Body = ToHttpBody(body);
                if (body.ContentType is not null)
                    SetContentTypeIfMissing(request, body.ContentType);
            }
            return request;
        }
        if (binding.InputBodyCodec is { } codec)
        {
            request.Body = ToHttpBody(codec.Serialize(input!));
            SetContentTypeIfMissing(request, binding.BodyContentType);
        }

        return request;
    }

    public static TInput DeserializeRequest<TInput, TOutput, TInputBuilder, TOutputBuilder>(
        RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> binding,
        SmithyHttpRequest request
    )
        where TInputBuilder : notnull
        where TOutputBuilder : notnull
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);

        CheckContentType(binding, request);
        CheckAccept(binding, request);

        var builder = binding.InputSchema.CreateTypedBuilder();
        var labels = ExtractLabels(binding.UriTemplate, request.RequestUri);
        var query = ParseQuery(request.RequestUri);

        // A member bound to the URI or headers is never seen by the body codec, so this is the only
        // place its absence is still observable: once the builder is finalized a missing value-type
        // member has already defaulted, and neither the finalizer nor the validator can tell it from
        // a value the caller actually sent.
        foreach (var member in binding.LabelMembers)
        {
            if (labels.TryGetValue(member.Name, out var labelValue))
                member.SetObject(
                    builder,
                    ParseHttpValue(member.Target, member.MemberTraits, labelValue)
                );
            else if (member.IsRequired)
                throw new MissingRequiredMemberException(member.Name);
        }
        foreach (var (member, headerName) in binding.RequestHeaderMembers)
        {
            if (TryGetFirstHeader(request.Headers, headerName, out var header))
                member.SetObject(
                    builder,
                    ParseHttpBindingValue(member.Target, member.MemberTraits, header)
                );
            else if (member.IsRequired)
                throw new MissingRequiredMemberException(member.Name);
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
                    ParseHttpBindingValues(member.Target, member.MemberTraits, values)
                );
            else if (member.IsRequired)
                throw new MissingRequiredMemberException(member.Name);
        }
        if (binding.QueryParamsMember is { } qpMember)
            // On clients, explicitly bound @httpQuery members take precedence over entries in an
            // @httpQueryParams map. On servers the map represents the request as received and must
            // include every query parameter, including names that are also explicitly bound.
            qpMember.SetObject(builder, ReadQueryParams(qpMember, query));
        if (binding.InputPayloadReader is { } readPayload)
            readPayload(BodyBytesOrNull(request.Body), BodyStreamOrNull(request.Body), builder);
        else if (
            binding.InputBodyCodec is { } codec
            && BodyBytesOrNull(request.Body) is { Length: > 0 } content
        )
            codec.ReadInto(content, builder);

        return binding.InputSchema.Build(builder);
    }

    public static SmithyHttpServerResponse SerializeResponse<
        TInput,
        TOutput,
        TInputBuilder,
        TOutputBuilder
    >(RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> binding, TOutput output)
        where TInputBuilder : notnull
        where TOutputBuilder : notnull
    {
        ArgumentNullException.ThrowIfNull(binding);

        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var statusCode = binding.SuccessStatusCode;

        if (binding.ResponseCodeMember is { } codeMember)
        {
            var value = codeMember.GetObject(output!);
            if (value is not null)
                statusCode = (int)ParseHttpValue(Schemas.Integer, value.ToString()!)!;
        }
        foreach (var (member, headerName) in binding.ResponseHeaderMembers)
            AddHeader(headers, headerName, member, output!);
        if (binding.ResponsePrefixHeadersMember is { } respPhMember)
            AddPrefixedHeaders(headers, respPhMember.Prefix, respPhMember.Member, output!);

        SmithyHttpBody responseBody = SmithyHttpBody.Empty;
        if (binding.OutputPayloadWriter is { } writePayload)
        {
            var body = writePayload(output!);
            responseBody = ToHttpBody(body);
            if (body.ContentType is not null)
                contentHeaders["Content-Type"] = [body.ContentType];
        }
        else if (binding.OutputBodyCodec is { } codec)
        {
            responseBody = ToHttpBody(codec.Serialize(output!));
            contentHeaders["Content-Type"] = [binding.BodyContentType];
        }

        return ToServerResponse(statusCode, responseBody, headers, contentHeaders);
    }

    public static TOutput DeserializeResponse<TInput, TOutput, TInputBuilder, TOutputBuilder>(
        RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> binding,
        SmithyHttpClientResponse response
    )
        where TInputBuilder : notnull
        where TOutputBuilder : notnull
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(response);

        var builder = binding.OutputSchema.CreateTypedBuilder();

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
                    ParseHttpBindingValue(member.Target, member.MemberTraits, header)
                );
            }
        }
        if (binding.ResponsePrefixHeadersMember is { } respPhMember)
            respPhMember.Member.SetObject(
                builder,
                ReadPrefixedHeaders(respPhMember.Member, response.Headers, respPhMember.Prefix)
            );
        if (binding.OutputPayloadReader is { } readPayload)
            readPayload(BodyBytesOrNull(response.Body), BodyStreamOrNull(response.Body), builder);
        else if (binding.OutputBodyCodec is { } codec && response.Content.Length > 0)
            codec.ReadInto(response.Content, builder);

        return binding.OutputSchema.Build(builder);
    }

    internal static HttpOperationError[] CompileErrorDeserializers(
        IReadOnlyList<IOperationErrorSchema> errors,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    )
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(codecFactory);
        return errors
            .Select(error =>
                (HttpOperationError)CompileErrorDeserializer(
                    (dynamic)error,
                    codecFactory,
                    rawStringPayloads
                )
            )
            .ToArray();
    }

    private static HttpOperationError CompileErrorDeserializer<TError>(
        OperationErrorSchema<TError> error,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    )
        where TError : Exception
    {
        if (error.Schema.Resolved is not IStructSchema<TError> schema)
        {
            throw new InvalidOperationException(
                $"Error schema '{error.Schema.Id}' must be a structure schema."
            );
        }

        return CompileErrorDeserializer(error, (dynamic)schema, codecFactory, rawStringPayloads);
    }

    private static HttpOperationError CompileErrorDeserializer<TError, TBuilder>(
        OperationErrorSchema<TError> error,
        IStructSchema<TError, TBuilder> schema,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    )
        where TError : Exception
        where TBuilder : notnull
    {
        IMemberSchema<TError>? responseCodeMember = null;
        var headerMembers = new List<HeaderMemberBinding>();
        var prefixHeaderMembers = new List<PrefixHeaderMemberBinding>();
        RestPayloadReader? payloadReader = null;
        var bodyMembers = new List<IMemberSchema<TError>>();

        foreach (var member in Schemas.GetMembers(schema))
        {
            if (member.MemberTraits.ContainsKey(RestTraits.HttpResponseCode))
            {
                responseCodeMember = member;
            }
            else if (member.MemberTraits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                headerMembers.Add(new HeaderMemberBinding(member, headerTrait.Value.AsString()));
            }
            else if (
                member.MemberTraits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait)
            )
            {
                prefixHeaderMembers.Add(
                    new PrefixHeaderMemberBinding(member, prefixTrait.Value.AsString())
                );
            }
            else if (member.MemberTraits.ContainsKey(RestTraits.HttpPayload))
            {
                payloadReader = BuildPayloadReader(member, codecFactory, rawStringPayloads);
            }
            else
            {
                bodyMembers.Add(member);
            }
        }

        var bodyCodec =
            bodyMembers.Count > 0
                ? codecFactory.CodecFor(
                    Schemas.Project(schema, bodyMembers),
                    materializeTopLevelDefaults: true
                )
                : null;

        return new HttpOperationError(
            error.Id,
            error.HttpStatusCode,
            response =>
            {
                var builder = schema.CreateTypedBuilder();
                if (responseCodeMember is not null)
                {
                    responseCodeMember.SetObject(
                        builder,
                        ParseHttpValue(
                            responseCodeMember.Target,
                            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                        )
                    );
                }

                foreach (var (member, headerName) in headerMembers)
                {
                    if (
                        TryGetFirstHeader(response.Headers, headerName, out var header)
                        || TryGetFirstHeader(response.ContentHeaders, headerName, out header)
                    )
                    {
                        member.SetObject(
                            builder,
                            ParseHttpBindingValue(member.Target, member.MemberTraits, header)
                        );
                    }
                }

                foreach (var (member, prefix) in prefixHeaderMembers)
                {
                    member.SetObject(
                        builder,
                        ReadPrefixedHeaders(member, response.Headers, prefix)
                    );
                }

                payloadReader?.Invoke(
                    BodyBytesOrNull(response.Body),
                    BodyStreamOrNull(response.Body),
                    builder
                );
                if (bodyCodec is not null && response.Content.Length > 0)
                {
                    bodyCodec.ReadInto(codecFactory.PrepareErrorBody(response.Content), builder);
                }

                return schema.Build(builder);
            }
        );
    }

    /// <summary>
    /// Serializes a modeled error into a REST error response: the supplied HTTP status code, an
    /// <c>X-Amzn-Errortype</c> discriminator carrying the error's shape name, the error's HTTP
    /// header/payload bindings, and a body holding the remaining members (at minimum <c>{}</c>).
    /// </summary>
    public static SmithyHttpServerResponse SerializeError<TError>(
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

        return CompileErrorSerializer(
            errorSchema,
            codecFactory,
            rawStringPayloads,
            errorTypeHeader
        )(value, errorShapeId, statusCode);
    }

    /// <summary>
    /// Compiles an error shape into a response writer, resolving everything the shape determines —
    /// which members bind to headers, which to the body, the projected body codec — once instead of
    /// per response.
    /// </summary>
    /// <remarks>
    /// <see cref="SerializeError"/> compiles on every call, so it is the ad-hoc entry point; anything
    /// serializing the same error repeatedly should hold the result of this instead. The shape id and
    /// status code stay parameters rather than being baked in, because the malformed-request schema
    /// is one shape serving several of each.
    /// </remarks>
    internal static RestErrorSerializer<TError> CompileErrorSerializer<TError>(
        Schema<TError> errorSchema,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        string errorTypeHeader
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(codecFactory);

        if (errorSchema.Resolved is not IStructSchema<TError> schema)
        {
            throw new InvalidOperationException(
                $"Error schema '{errorSchema.Id}' must be a structure schema."
            );
        }

        return CompileStructuredError(
            (dynamic)schema,
            codecFactory,
            rawStringPayloads,
            errorTypeHeader
        );
    }

    private static RestErrorSerializer<TError> CompileStructuredError<TError, TBuilder>(
        IStructSchema<TError, TBuilder> schema,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        string errorTypeHeader
    )
        where TBuilder : notnull
    {
        var headerMembers = new List<(string Name, IMemberSchema<TError> Member)>();
        var prefixHeaderMembers = new List<(string Prefix, IMemberSchema<TError> Member)>();
        var bodyMembers = new List<IMemberSchema<TError>>();
        IMemberSchema<TError>? payloadMember = null;

        foreach (var member in Schemas.GetMembers(schema))
        {
            if (member.MemberTraits.ContainsKey(RestTraits.HttpResponseCode))
            {
                continue;
            }

            if (member.MemberTraits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                headerMembers.Add((headerTrait.Value.AsString(), member));
            }
            else if (
                member.MemberTraits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait)
            )
            {
                prefixHeaderMembers.Add((prefixTrait.Value.AsString(), member));
            }
            else if (member.MemberTraits.ContainsKey(RestTraits.HttpPayload))
            {
                payloadMember = member;
            }
            else
            {
                bodyMembers.Add(member);
            }
        }

        var headerBindings = headerMembers.ToArray();
        var prefixHeaderBindings = prefixHeaderMembers.ToArray();

        // The body writer is where the bulk of the per-response cost was: without this the projected
        // schema was rebuilt and a whole codec recompiled for every error served.
        Func<TError, RestBody> writeBody;
        if (payloadMember is not null)
        {
            writeBody = BuildPayloadWriter(
                payloadMember,
                codecFactory,
                rawStringPayloads,
                emptyStructOnNull: false
            );
        }
        else
        {
            var codec = codecFactory.CodecFor(
                Schemas.Project(schema, bodyMembers),
                materializeTopLevelDefaults: true
            );
            var contentType = codecFactory.ContentType;
            writeBody = value => new RestBody(codec.Serialize(value), contentType);
        }

        return (value, errorShapeId, statusCode) =>
        {
            var headers = new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                [errorTypeHeader] = [LocalName(errorShapeId)],
            };
            foreach (var (name, member) in headerBindings)
            {
                AddHeader(headers, name, member, value);
            }

            foreach (var (prefix, member) in prefixHeaderBindings)
            {
                AddPrefixedHeaders(headers, prefix, member, value);
            }

            var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase
            );
            var body = writeBody(value);
            if (body.ContentType is not null)
            {
                contentHeaders["Content-Type"] = [body.ContentType];
            }

            return ToServerResponse(statusCode, ToHttpBody(body.Content), headers, contentHeaders);
        };
    }

    private static SmithyHttpServerResponse ToServerResponse(
        int statusCode,
        SmithyHttpBody body,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> contentHeaders
    )
    {
        var response = new SmithyHttpServerResponse
        {
            StatusCode = statusCode,
            Body = ToChunks(body),
            ContentLength = ContentLength(body),
        };
        foreach (var header in headers)
        {
            response.Headers[header.Key] = header.Value;
        }

        foreach (var header in contentHeaders)
        {
            response.Headers[header.Key] = header.Value;
        }

        return response;
    }

    private static IAsyncEnumerable<ReadOnlyMemory<byte>> ToChunks(SmithyHttpBody body) =>
        body switch
        {
            SmithyHttpBody.Bytes bytes => SingleChunk(bytes.Content),
            SmithyHttpBody.Streaming streaming => ReadStream(streaming.Content),
            SmithyHttpBody.EventStreaming eventStreaming => eventStreaming.Content,
            _ => AsyncEnumerable.Empty<ReadOnlyMemory<byte>>(),
        };

    private static long? ContentLength(SmithyHttpBody body) =>
        body switch
        {
            SmithyHttpBody.Bytes bytes => bytes.Content.Length,
            SmithyHttpBody.Streaming streaming => streaming.ContentLength,
            SmithyHttpBody.EventStreaming => null,
            _ => 0L,
        };

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> SingleChunk(
        ReadOnlyMemory<byte> chunk
    )
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return chunk;
    }

    // The buffer is reused across iterations: valid because the host writer consumes each chunk
    // before pulling the next.
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadStream(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            yield return buffer.AsMemory(0, read);
        }
    }

    private static SmithyHttpBody ToHttpBody(RestBody body) =>
        body.EventStreamingContent is not null
            ? new SmithyHttpBody.EventStreaming(body.EventStreamingContent)
        : body.StreamingContent is not null
            ? new SmithyHttpBody.Streaming(body.StreamingContent, body.StreamingContentLength)
        : ToHttpBody(body.Content);

    private static SmithyHttpBody ToHttpBody(byte[] content) =>
        content.Length == 0 ? SmithyHttpBody.Empty : new SmithyHttpBody.Bytes(content);

    private static byte[]? BodyBytesOrNull(SmithyHttpBody body) =>
        body is SmithyHttpBody.Bytes bytes ? bytes.Content : null;

    private static Stream? BodyStreamOrNull(SmithyHttpBody body) =>
        body is SmithyHttpBody.Streaming stream ? stream.Content : null;

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> FrameEventsAsync<TEvent>(
        IAsyncEnumerable<TEvent> events,
        ICodec<TEvent> codec,
        IUnionSchema eventSchema,
        string payloadContentType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var value in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            var eventType = eventSchema.GetCaseObject(value!).Name;
            yield return CreateEventStreamMessage(
                    eventType,
                    codec.Serialize(value),
                    payloadContentType
                )
                .Encode();
        }
    }

    private static async IAsyncEnumerable<TEvent> ReadEventsAsync<TEvent>(
        Stream stream,
        ICodec<TEvent> codec,
        string payloadContentType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await using (stream.ConfigureAwait(false))
        {
            await foreach (
                var message in EventStreamMessageReader
                    .ReadAllAsync(stream, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                var value = DeserializeEventMessage(codec, message, payloadContentType);
                if (value is not null)
                {
                    yield return value;
                }
            }
        }
    }

    private static EventStreamMessage CreateEventStreamMessage(
        string eventType,
        ReadOnlyMemory<byte> payload,
        string payloadContentType
    ) =>
        new(
            new Dictionary<string, EventStreamHeaderValue>
            {
                [EventStreamHeaders.MessageType] = new EventStreamHeaderValue.Text(
                    EventStreamHeaders.EventMessageType
                ),
                [EventStreamHeaders.EventType] = new EventStreamHeaderValue.Text(eventType),
                [EventStreamHeaders.ContentType] = new EventStreamHeaderValue.Text(
                    payloadContentType
                ),
            },
            payload
        );

    private static TEvent? DeserializeEventMessage<TEvent>(
        ICodec<TEvent> codec,
        EventStreamMessage message,
        string payloadContentType
    )
    {
        EnsureEventMessage(message);
        EnsureEventPayload(message, payloadContentType);
        return codec.Deserialize(message.Payload.ToArray());
    }

    private static void EnsureEventMessage(EventStreamMessage message)
    {
        var messageType = message.StringHeader(EventStreamHeaders.MessageType);
        if (
            string.Equals(
                messageType,
                EventStreamHeaders.EventMessageType,
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        ThrowEventStreamException(message);
    }

    private static void EnsureEventPayload(EventStreamMessage message, string payloadContentType)
    {
        var contentType = message.StringHeader(EventStreamHeaders.ContentType);
        if (
            contentType is not null
            && !contentType.StartsWith(payloadContentType, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException(
                $"Expected REST event payload content type '{payloadContentType}' but received '{contentType}'."
            );
        }
    }

    private static void ThrowEventStreamException(EventStreamMessage message)
    {
        var messageType = message.StringHeader(EventStreamHeaders.MessageType);
        if (
            string.Equals(
                messageType,
                EventStreamHeaders.ErrorMessageType,
                StringComparison.Ordinal
            )
        )
        {
            var code = message.StringHeader(EventStreamHeaders.ErrorCode) ?? "UnknownError";
            var text = message.StringHeader(EventStreamHeaders.ErrorMessage);
            throw new InvalidOperationException(
                string.IsNullOrEmpty(text) ? code : $"{code}: {text}"
            );
        }

        if (
            string.Equals(
                messageType,
                EventStreamHeaders.ExceptionMessageType,
                StringComparison.Ordinal
            )
        )
        {
            var type = message.StringHeader(EventStreamHeaders.ExceptionType) ?? "UnknownException";
            throw new InvalidOperationException($"REST event stream exception: {type}.");
        }

        throw new InvalidDataException(
            $"Unknown REST event stream message type '{messageType ?? "<missing>"}'."
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
            _ when target.Resolved is IEventStreamSchema => EventStreamContentType,
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
    internal static RestPayloadReader BuildPayloadReader<TContainer>(
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
            var traits = member.MemberTraits;
            var mediaType = GetMediaType(target, traits);
            var kind = UnwrapNullable(target).Kind;

            if (target.Resolved is IEventStreamSchema eventStream)
            {
                Result = BuildEventStreamPayloadWriter(
                    member,
                    (dynamic)eventStream.EventSchema,
                    codecFactory
                );
                return;
            }

            if (kind == ShapeKind.Blob && traits.ContainsKey(RestTraits.Streaming))
            {
                var contentType = mediaType ?? codecFactory.BlobContentType;
                var requiresLength = traits.ContainsKey(RestTraits.RequiresLength);
                Result = container =>
                {
                    if (member.GetValue(container) is not { } value)
                    {
                        return RestBody.None;
                    }

                    var stream = (Stream)(object)value;
                    if (!requiresLength)
                    {
                        return new RestBody([], contentType, stream);
                    }

                    if (!stream.CanSeek)
                    {
                        throw new InvalidOperationException(
                            "Streaming blob payloads with @requiresLength require a seekable stream."
                        );
                    }

                    return new RestBody([], contentType, stream, stream.Length - stream.Position);
                };
                return;
            }

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
            var codec = codecFactory.CodecFor(target, traits);
            var jsonContentType = codecFactory.ContentType;
            var writeEmptyStructOnNull = emptyStructOnNull;

            Result = container =>
            {
                var value = member.GetValue(container);
                if (value is null)
                {
                    return
                        writeEmptyStructOnNull
                        && UnwrapNullable(target).Resolved is IStructSchema<TValue> emptyStruct
                        ? new RestBody(codec.Serialize(emptyStruct.BuildEmpty()), jsonContentType)
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
        public RestPayloadReader? Result { get; private set; }

        public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
        {
            var target = member.TargetSchema;
            var traits = member.MemberTraits;
            var unwrapped = UnwrapNullable(target);

            if (target.Resolved is IEventStreamSchema eventStream)
            {
                Result = BuildEventStreamPayloadReader(
                    member,
                    (dynamic)eventStream.EventSchema,
                    codecFactory
                );
                return;
            }

            if (unwrapped.Kind == ShapeKind.Blob && traits.ContainsKey(RestTraits.Streaming))
            {
                Result = (content, streamingContent, builder) =>
                {
                    if (streamingContent is not null)
                    {
                        member.SetObject(builder, streamingContent);
                    }
                    else if (content is null or { Length: 0 })
                    {
                        if (traits.ContainsKey(DefaultTrait))
                        {
                            member.SetObject(builder, Stream.Null);
                        }
                    }
                    else
                    {
                        member.SetObject(builder, new MemoryStream(content, writable: false));
                    }
                };
                return;
            }

            if (unwrapped.Kind == ShapeKind.Blob)
            {
                Result = (content, streamingContent, builder) =>
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
                Result = (content, streamingContent, builder) =>
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

            var codec = codecFactory.CodecFor(target, traits);
            Result = (content, streamingContent, builder) =>
            {
                if (content is null or { Length: 0 })
                    ApplyDefault(member, target, traits, builder);
                else
                    member.SetObject(builder, codec.Deserialize(content));
            };
        }

        private static void ApplyDefault(
            IStructMemberSchema member,
            Schema target,
            IReadOnlyDictionary<ShapeId, Trait> traits,
            object builder
        )
        {
            if (TryCreateDefaultValue(target, traits, out var defaultValue))
                member.SetObject(builder, defaultValue);
        }
    }

    private static Func<TContainer, RestBody> BuildEventStreamPayloadWriter<
        TContainer,
        TValue,
        TEvent
    >(
        IMemberSchema<TContainer, TValue> member,
        Schema<TEvent> eventSchema,
        IRestBodyCodecFactory codecFactory
    )
    {
        var codec = codecFactory.CodecFor(eventSchema);
        var union =
            eventSchema.Resolved as IUnionSchema
            ?? throw new InvalidOperationException(
                "REST event stream payloads must target a union schema."
            );

        return container =>
        {
            var value = member.GetValue(container);
            if (value is null)
            {
                return RestBody.None;
            }

            return new RestBody(
                [],
                EventStreamContentType,
                EventStreamingContent: FrameEventsAsync(
                    (IAsyncEnumerable<TEvent>)(object)value,
                    codec,
                    union,
                    codecFactory.ContentType
                )
            );
        };
    }

    private static RestPayloadReader BuildEventStreamPayloadReader<TContainer, TValue, TEvent>(
        IMemberSchema<TContainer, TValue> member,
        Schema<TEvent> eventSchema,
        IRestBodyCodecFactory codecFactory
    )
    {
        var codec = codecFactory.CodecFor(eventSchema);
        var payloadContentType = codecFactory.ContentType;
        return (content, streamingContent, builder) =>
        {
            var stream =
                streamingContent
                ?? (
                    content is { Length: > 0 }
                        ? new MemoryStream(content, writable: false)
                        : Stream.Null
                );
            member.SetObject(
                builder,
                (TValue)(object)ReadEventsAsync(stream, codec, payloadContentType)
            );
        };
    }

    private static string BuildRequestUri<TInput>(
        string uriTemplate,
        IReadOnlyList<IStructMemberSchema> labelMembers,
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
                    EscapeGreedyLabel(member.Target, member.MemberTraits, value),
                    StringComparison.Ordinal
                )
                .Replace(
                    "{" + member.Name + "}",
                    Uri.EscapeDataString(
                        FormatHttpValue(member.Target, member.MemberTraits, value)
                    ),
                    StringComparison.Ordinal
                );
        }

        return requestUri;
    }

    private static void AddHeader<TInput>(
        Dictionary<string, IReadOnlyList<string>> headers,
        string name,
        IStructMemberSchema member,
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
        IStructMemberSchema member,
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
        IStructMemberSchema member,
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
        IStructMemberSchema member,
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
                || (prefix.Length == 0 && IsTransportManagedHeader(header.Key))
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
        IStructMemberSchema member,
        Dictionary<string, IReadOnlyList<string>> query
    )
    {
        var mapSchema = RequireMap(member);
        var builder = mapSchema.CreateBuilder();
        foreach (var entry in query)
        {
            if (entry.Value.Count == 0)
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

    private static bool IsTransportManagedHeader(string name) =>
        name.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase);

    private static string AppendQuery<TInput>(
        string requestUri,
        string name,
        IStructMemberSchema member,
        TInput input
    )
    {
        var value = member.GetObject(input!);
        if (value is null)
        {
            return requestUri;
        }

        var builder = new StringBuilder(requestUri);
        AppendQueryValue(builder, name, member.Target, member.MemberTraits, value);
        return builder.ToString();
    }

    private static string AppendQueryParams<TInput>(
        string requestUri,
        IStructMemberSchema member,
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

    private static IMapSchema RequireMap(IStructMemberSchema member) =>
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
        builder.Append(builder.ToString().Contains('?', StringComparison.Ordinal) ? '&' : '?');
        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(FormatHttpValue(schema, traits, value)));
    }

    private static string FormatHttpHeaderValue(IStructMemberSchema member, object value)
    {
        if (member.Target.Resolved is IListSchema listSchema)
        {
            return string.Join(
                ", ",
                listSchema
                    .GetElementsObject(value)
                    .Where(element => element is not null)
                    .Select(element =>
                        FormatHttpHeaderListValue(listSchema.Element, member.MemberTraits, element!)
                    )
            );
        }

        return FormatHttpValue(member.Target, member.MemberTraits, value);
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
        try
        {
            return ParseHttpValueCore(schema, traits, value);
        }
        catch (Exception exception)
            when (exception is FormatException or OverflowException or ArgumentException)
        {
            // A label, query parameter, or header the model types but the caller did not: on a
            // server the caller's mistake, answered with a structured 400 rather than a fault.
            throw MalformedRequestException.Serialization(
                $"Value '{value}' is not a valid {schema.Kind.ToString().ToLowerInvariant()}."
            );
        }
    }

    private static object? ParseHttpValueCore(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        string value
    )
    {
        return schema.Kind switch
        {
            // Only the two literals the model means. bool.Parse also accepts "True" and " TRUE ",
            // which would let a caller coerce a string into a boolean the model never declared.
            ShapeKind.Boolean => value switch
            {
                "true" => true,
                "false" => false,
                _ => throw new FormatException($"'{value}' is not a boolean."),
            },
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
            // A label, query parameter, or header is only ever read from a request, so there is no
            // looser peer to accommodate here the way there is in a response body.
            "date-time" => Rfc3339.Parse(value, WireReadMode.Strict),
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

    /// <summary>
    /// Whether an <c>@httpPayload</c> member carries bytes the protocol assigns no meaning to: a blob
    /// the model does not give a <c>@mediaType</c>. <c>application/octet-stream</c> is what such a
    /// payload is written as, not what it has to be read as, so a caller may label those bytes
    /// however it likes and a caller's <c>Accept</c> constrains nothing.
    /// </summary>
    internal static bool IsOpaquePayload(
        Schema target,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) =>
        GetMediaType(target, traits) is null
        && target.Resolved is not IEventStreamSchema
        && UnwrapNullable(target).Kind == ShapeKind.Blob;

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

    /// <summary>
    /// Rejects a request body the operation cannot read. The model fixes exactly one media type per
    /// operation — the body codec's for a structured body, the <c>@mediaType</c> or implied type for
    /// an <c>@httpPayload</c> — so anything else is a 415 rather than something to guess at, and so
    /// is a body sent to an operation that reads none.
    /// </summary>
    private static void CheckContentType<TInput, TOutput, TInputBuilder, TOutputBuilder>(
        RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> binding,
        SmithyHttpRequest request
    )
        where TInputBuilder : notnull
        where TOutputBuilder : notnull
    {
        var declared = TryGetFirstHeader(request.Headers, "Content-Type", out var header)
            ? header
            : request.ContentType;

        if (binding.RequestContentType is not { } expected)
        {
            // No media type is the right one for an operation that reads no body, so a caller that
            // announces one is describing a body the server has nowhere to put. Both halves matter:
            // a Content-Type with no body is a stray header, and a body with no Content-Type
            // announces nothing — neither is a media type the server would have to reject.
            if (declared is not null && HasRequestContent(request))
            {
                throw MalformedRequestException.UnsupportedMediaType(
                    $"Operation reads no request body, but one arrived as '{declared}'."
                );
            }

            return;
        }

        if (binding.RequestMediaTypeIsOpaque)
        {
            return;
        }

        // An absent Content-Type is only an omission once there is something to describe: a request
        // that sends no body at all leaves an optional payload unset, which the model allows.
        if (declared is null)
        {
            if (binding.RequiresDeclaredContentType && HasRequestContent(request))
            {
                throw MalformedRequestException.UnsupportedMediaType(
                    $"Request body requires Content-Type '{expected}' but none was set."
                );
            }

            return;
        }

        if (!MediaTypeEquals(MediaTypeOf(declared), expected))
        {
            throw MalformedRequestException.UnsupportedMediaType(
                $"Expected Content-Type '{expected}' but found '{declared}'."
            );
        }
    }

    /// <summary>
    /// Rejects a request whose <c>Accept</c> excludes the only media type the operation's response
    /// can use. An operation with no modeled output has no response media type to negotiate over.
    /// </summary>
    private static void CheckAccept<TInput, TOutput, TInputBuilder, TOutputBuilder>(
        RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> binding,
        SmithyHttpRequest request
    )
        where TInputBuilder : notnull
        where TOutputBuilder : notnull
    {
        if (
            binding.OutputIsUnit
            || binding.ResponseMediaTypeIsOpaque
            || !TryGetFirstHeader(request.Headers, "Accept", out var accept)
            || accept.Length == 0
        )
        {
            return;
        }

        foreach (var range in accept.Split(','))
        {
            if (MediaRangeAccepts(MediaTypeOf(range), binding.AcceptType))
            {
                return;
            }
        }

        throw MalformedRequestException.NotAcceptable(
            $"Response is '{binding.AcceptType}', which Accept '{accept}' excludes."
        );
    }

    private static bool HasRequestContent(SmithyHttpRequest request) =>
        request.Body switch
        {
            SmithyHttpBody.Bytes bytes => bytes.Content.Length > 0,
            // A streaming body is not read here, so its length is all there is to go on. An unknown
            // length (a chunked request) counts as content: the caller is sending something.
            SmithyHttpBody.Streaming streaming => streaming.ContentLength is null or > 0,
            SmithyHttpBody.EventStreaming => true,
            _ => false,
        };

    /// <summary>The media type without its parameters — <c>application/json; charset=utf-8</c> is JSON.</summary>
    private static ReadOnlySpan<char> MediaTypeOf(string headerValue)
    {
        var span = headerValue.AsSpan();
        var separator = span.IndexOf(';');
        return (separator < 0 ? span : span[..separator]).Trim();
    }

    private static bool MediaTypeEquals(ReadOnlySpan<char> declared, string expected) =>
        declared.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool MediaRangeAccepts(ReadOnlySpan<char> range, string mediaType)
    {
        if (range.Equals("*/*", StringComparison.Ordinal) || range.IsEmpty)
        {
            return true;
        }

        if (range.EndsWith("/*", StringComparison.Ordinal))
        {
            var type = range[..^2];
            var slash = mediaType.IndexOf('/', StringComparison.Ordinal);
            return slash > 0
                && type.Equals(mediaType.AsSpan(0, slash), StringComparison.OrdinalIgnoreCase);
        }

        return MediaTypeEquals(range, mediaType);
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
