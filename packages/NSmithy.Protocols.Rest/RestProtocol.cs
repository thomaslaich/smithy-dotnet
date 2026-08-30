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
public interface IRestBodyCodecFactory : IProjectionCodecFactory
{
    string ContentType { get; }

    string BlobContentType { get; }

    byte[] PrepareErrorBody(byte[] content) => content;
}

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
        var bound = binding.Input;

        var uri = new HttpUriBuilder(binding.UriTemplate);
        foreach (var label in bound.LabelWriters)
        {
            label.Write(uri, input);
        }

        foreach (var query in bound.QueryWriters)
        {
            query.Write(uri, input);
        }

        bound.QueryParamsWriter?.Write(uri, input, bound.BoundQueryNames);

        var request = new SmithyHttpRequest(binding.HttpMethod, uri.ToString());
        request.Headers["Accept"] = [binding.AcceptType];
        request.ExpectStreamingResponse = binding.OutputHasStreamingPayload;

        foreach (var header in bound.HeaderWriters)
        {
            if (header.Format(input) is not { } value)
            {
                continue;
            }

            switch (header.Slot)
            {
                case HeaderSlot.ContentType:
                    request.ContentType = value;
                    break;
                case HeaderSlot.ContentHeaders:
                    request.ContentHeaders[header.Name] = [value];
                    break;
                default:
                    request.Headers[header.Name] = [value];
                    break;
            }
        }

        bound.PrefixHeaderWriter?.Write(request.Headers, input);

        if (bound.PayloadWriter is { } writePayload)
        {
            var body = writePayload(input);
            if (body.HasContent)
            {
                request.Body = ToHttpBody(body);
                if (body.ContentType is not null)
                {
                    SetContentTypeIfMissing(request, body.ContentType);
                }
            }

            return request;
        }

        if (bound.BodyCodec is { } codec)
        {
            request.Body = ToHttpBody(codec.Serialize(input));
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

        var bound = binding.Input;
        var builder = bound.Schema.CreateTypedBuilder();
        var labels = ExtractLabels(binding.UriTemplate, request.RequestUri);
        var query = ParseQuery(request.RequestUri);

        // A member bound to the URI or headers is never seen by the body codec, so this is the only
        // place its absence is still observable: once the builder is finalized a missing value-type
        // member has already defaulted, and neither the finalizer nor the validator can tell it from
        // a value the caller actually sent.
        foreach (var label in bound.LabelReaders)
        {
            if (labels.TryGetValue(label.Name, out var value))
            {
                label.Read(builder, value);
            }
            else if (label.IsRequired)
            {
                throw new MissingRequiredMemberException(label.Name);
            }
        }

        foreach (var header in bound.HeaderReaders)
        {
            if (TryGetFirstHeader(request.Headers, header.Name, out var value))
            {
                header.Read(builder, value);
            }
            else if (header.IsRequired)
            {
                throw new MissingRequiredMemberException(header.MemberName);
            }
        }

        bound.PrefixHeaderReader?.Read(builder, request.Headers);

        foreach (var parameter in bound.QueryReaders)
        {
            if (query.TryGetValue(parameter.Name, out var values) && values.Count > 0)
            {
                parameter.Read(builder, values);
            }
            else if (parameter.IsRequired)
            {
                throw new MissingRequiredMemberException(parameter.MemberName);
            }
        }

        // On clients, explicitly bound @httpQuery members take precedence over entries in an
        // @httpQueryParams map. On servers the map represents the request as received and must
        // include every query parameter, including names that are also explicitly bound.
        bound.QueryParamsReader?.Read(builder, query);

        if (bound.PayloadReader is { } readPayload)
        {
            readPayload(BodyBytesOrNull(request.Body), BodyStreamOrNull(request.Body), builder);
        }
        else if (
            bound.BodyCodec is { } codec
            && BodyBytesOrNull(request.Body) is { Length: > 0 } content
        )
        {
            codec.ReadInto(content, builder);
        }

        return bound.Schema.Build(builder);
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
        var bound = binding.Output;

        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var statusCode = bound.StatusCodeWriter?.Get(output) ?? binding.SuccessStatusCode;
        WriteHeaders(bound, output, headers);

        var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        SmithyHttpBody responseBody = SmithyHttpBody.Empty;
        if (bound.PayloadWriter is { } writePayload)
        {
            var body = writePayload(output);
            responseBody = ToHttpBody(body);
            if (body.ContentType is not null)
            {
                contentHeaders["Content-Type"] = [body.ContentType];
            }
        }
        else if (bound.BodyCodec is { } codec)
        {
            responseBody = ToHttpBody(codec.Serialize(output));
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
        return ReadResponse(binding.Output, response, prepareBody: null);
    }

    private static void WriteHeaders<T, TBuilder>(
        RestStructBinding<T, TBuilder> bound,
        T value,
        Dictionary<string, IReadOnlyList<string>> headers
    )
        where TBuilder : notnull
    {
        foreach (var header in bound.HeaderWriters)
        {
            if (header.Format(value) is { } text)
            {
                headers[header.Name] = [text];
            }
        }

        bound.PrefixHeaderWriter?.Write(headers, value);
    }

    private static T ReadResponse<T, TBuilder>(
        RestStructBinding<T, TBuilder> bound,
        SmithyHttpClientResponse response,
        Func<byte[], byte[]>? prepareBody
    )
        where TBuilder : notnull
    {
        var builder = bound.Schema.CreateTypedBuilder();
        bound.StatusCodeReader?.Read(builder, (int)response.StatusCode);
        foreach (var header in bound.HeaderReaders)
        {
            if (
                TryGetFirstHeader(response.Headers, header.Name, out var value)
                || TryGetFirstHeader(response.ContentHeaders, header.Name, out value)
            )
            {
                header.Read(builder, value);
            }
        }

        bound.PrefixHeaderReader?.Read(builder, response.Headers);

        if (bound.PayloadReader is { } readPayload)
        {
            readPayload(BodyBytesOrNull(response.Body), BodyStreamOrNull(response.Body), builder);
        }
        else if (bound.BodyCodec is { } codec && response.Content.Length > 0)
        {
            codec.ReadInto(
                prepareBody is null ? response.Content : prepareBody(response.Content),
                builder
            );
        }

        return bound.Schema.Build(builder);
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
        var bound = RestStructBinding<TError, TBuilder>.Compile(
            schema,
            HttpBindingSide.Response,
            codecFactory,
            rawStringPayloads,
            emptyStructOnNullPayload: false
        );
        if (bound.PayloadMember is null && bound.BodyMemberNames.Count > 0)
        {
            bound.BodyCodec = bound.CompileBodyCodec(
                codecFactory,
                new CodecFactoryOptions { MaterializeTopLevelDefaults = true }
            );
        }

        return new HttpOperationError(
            error.Id,
            error.HttpStatusCode,
            response => ReadResponse(bound, response, codecFactory.PrepareErrorBody)
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
        var bound = RestStructBinding<TError, TBuilder>.Compile(
            schema,
            HttpBindingSide.Response,
            codecFactory,
            rawStringPayloads,
            emptyStructOnNullPayload: false
        );

        // The body writer is where the bulk of the per-response cost was: without this the projected
        // schema was rebuilt and a whole codec recompiled for every error served.
        Func<TError, RestBody> writeBody;
        if (bound.PayloadWriter is { } writePayload)
        {
            writeBody = writePayload;
        }
        else
        {
            var codec = bound.CompileBodyCodec(
                codecFactory,
                new CodecFactoryOptions { MaterializeTopLevelDefaults = true }
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
            WriteHeaders(bound, value, headers);

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

    internal static async IAsyncEnumerable<ReadOnlyMemory<byte>> FrameEventsAsync<TEvent>(
        IAsyncEnumerable<TEvent> events,
        ICodec<TEvent> codec,
        Func<TEvent, string> eventTypeOf,
        string payloadContentType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var value in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return CreateEventStreamMessage(
                    eventTypeOf(value),
                    codec.Serialize(value),
                    payloadContentType
                )
                .Encode();
        }
    }

    internal static async IAsyncEnumerable<TEvent> ReadEventsAsync<TEvent>(
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

        return HttpBindingPlans.UnwrapNullable(target).Kind switch
        {
            _ when target.Resolved is IEventStreamSchema => EventStreamContentType,
            ShapeKind.Blob => codecFactory.BlobContentType,
            ShapeKind.String
            or ShapeKind.Enum when !UseBodyCodecForPayload(target, traits, rawStringPayloads) =>
                "text/plain",
            _ => codecFactory.ContentType,
        };
    }

    internal static bool UseBodyCodecForPayload(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        bool rawStringPayloads
    )
    {
        var kind = HttpBindingPlans.UnwrapNullable(schema).Kind;
        if (kind is not (ShapeKind.String or ShapeKind.Enum))
        {
            return true;
        }

        // String/enum payloads are raw text under restJson1/restXml; protocols that don't use raw
        // string payloads (alloy simpleRestJson) route them through the body codec, as do members
        // carrying an explicit @default.
        return traits.ContainsKey(DefaultTrait) || !rawStringPayloads;
    }

    internal static string? GetMediaType(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) =>
        (
            traits.TryGetValue(RestTraits.MediaType, out var trait)
                ? trait
                : schema.GetTrait(RestTraits.MediaType)
        )?.Value.AsString();

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
        && HttpBindingPlans.UnwrapNullable(target).Kind == ShapeKind.Blob;

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
