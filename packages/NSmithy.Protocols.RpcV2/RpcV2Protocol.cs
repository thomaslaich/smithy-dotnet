using System.Net;
using System.Runtime.CompilerServices;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.EventStream;
using NSmithy.Http;

namespace NSmithy.Protocols.RpcV2;

/// <summary>
/// The Smithy RPC v2 protocol family: every operation is a <c>POST</c> to
/// <c>/service/{Service}/operation/{Operation}</c> carrying a <c>Smithy-Protocol</c> header, with a
/// body in one serialization format. A subclass supplies that format; routing, framing, event
/// streams, and error dispatch are shared.
/// </summary>
public abstract class RpcV2Protocol : IProtocol
{
    private const string EventStreamContentType = "application/vnd.amazon.eventstream";
    private const string InitialRequestEventType = "initial-request";
    private const string InitialResponseEventType = "initial-response";

    private readonly IProjectionCodecFactory codecFactory;
    private readonly string contentType;
    private readonly string protocolHeader;
    private readonly string protocolName;
    private readonly CodecErrorReader errorReader;

    /// <param name="codecFactory">Creates the codecs for bodies and event payloads.</param>
    /// <param name="contentType">The media type of a buffered body, such as <c>application/cbor</c>.</param>
    /// <param name="protocolHeader">The <c>Smithy-Protocol</c> header value, such as <c>rpc-v2-cbor</c>.</param>
    /// <param name="protocolName">The protocol's name in error messages, such as <c>rpcv2Cbor</c>.</param>
    protected RpcV2Protocol(
        IProjectionCodecFactory codecFactory,
        string contentType,
        string protocolHeader,
        string protocolName
    )
    {
        ArgumentNullException.ThrowIfNull(codecFactory);
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(protocolHeader);
        ArgumentNullException.ThrowIfNull(protocolName);
        this.codecFactory = codecFactory;
        this.contentType = contentType;
        this.protocolHeader = protocolHeader;
        this.protocolName = protocolName;
        errorReader = new CodecErrorReader(
            codecFactory,
            response => RequiredBody(response.Content)
        );
    }

    /// <summary>
    /// Compiles the writer of an error's body: the error's members plus a <c>__type</c> entry
    /// holding <paramref name="errorShapeId"/>, the error's absolute shape id.
    /// </summary>
    protected abstract Func<TError, byte[]> CompileErrorBody<TError>(
        IStructSchema<TError> errorSchema,
        string errorShapeId
    );

    /// <summary>The <c>__type</c> of an error body, or null when it has none or is unreadable.</summary>
    protected abstract string? ReadErrorType(byte[] content);

    /// <summary>
    /// Binds the protocol to a service, yielding per-operation protocols. The service schema
    /// supplies the service shape name used to derive each operation's request path.
    /// </summary>
    public IServiceProtocol ForService(ServiceSchema service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new ServiceProtocol(this, service);
    }

    private sealed class ServiceProtocol(RpcV2Protocol protocol, ServiceSchema service)
        : IServiceProtocol
    {
        public IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        ) => CreateOperation(operation, isServer: false);

        public IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        ) => CreateOperation(operation, isServer: true);

        private OperationProtocol<TInput, TOutput> CreateOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation,
            bool isServer
        )
        {
            ArgumentNullException.ThrowIfNull(operation);

            // Decided once here rather than per shape: an event stream changes how the body is
            // framed, not what the operation's input structure has to satisfy. The events
            // themselves are not covered, since the validator skips the event-stream member and
            // rejecting one mid-stream needs a way to report it after the response has begun.
            var inputValidator = isServer ? SmithyValidator.FromSchema(operation.Input) : null;

            // Each direction is chosen independently; the four call shapes are their cross product.
            var request = EventStreamBinding.TryBind(
                operation.Input,
                new StreamingRequestCompiler<TInput>(protocol),
                out var streamingRequest
            )
                ? streamingRequest
                : protocol.UnaryRequestStrategy(operation.Input);
            var response = EventStreamBinding.TryBind(
                operation.Output,
                new StreamingResponseCompiler<TOutput>(protocol),
                out var streamingResponse
            )
                ? streamingResponse
                : protocol.UnaryResponseStrategy(operation.Output);

            return new OperationProtocol<TInput, TOutput>(
                protocol,
                service,
                operation,
                request,
                response,
                SmithyRequestModifiers.Compile(operation),
                inputValidator
            );
        }
    }

    private sealed class StreamingRequestCompiler<TInput>(RpcV2Protocol protocol)
        : IEventStreamBindingVisitor<TInput, RequestStrategy<TInput>>
    {
        public RequestStrategy<TInput> Visit<TBuilder, TEvent>(
            EventStreamBinding<TInput, TBuilder, TEvent> binding
        ) => protocol.StreamingRequestStrategy(binding);
    }

    private sealed class StreamingResponseCompiler<TOutput>(RpcV2Protocol protocol)
        : IEventStreamBindingVisitor<TOutput, ResponseStrategy<TOutput>>
    {
        public ResponseStrategy<TOutput> Visit<TBuilder, TEvent>(
            EventStreamBinding<TOutput, TBuilder, TEvent> binding
        ) => protocol.StreamingResponseStrategy(binding);
    }

    /// <summary>
    /// How one direction of an operation is put on and taken off the wire. Unlike gRPC, the two
    /// framings genuinely differ: a unary body is a bare document at the format's content type,
    /// while a stream is eventstream-framed, prefixed by an initial-request or initial-response
    /// message carrying the non-stream members. Resolving that once at bind time into these
    /// delegates is what lets a single operation protocol serve all four call shapes.
    /// </summary>
    private readonly record struct RequestStrategy<TInput>(
        Action<SmithyHttpRequest, TInput, CancellationToken> Write,
        Func<SmithyHttpRequest, CancellationToken, ValueTask<TInput>> Read,
        bool IsStreaming
    );

    /// <inheritdoc cref="RequestStrategy{TInput}" />
    private readonly record struct ResponseStrategy<TOutput>(
        Func<TOutput, CancellationToken, SmithyHttpServerResponse> Write,
        Func<SmithyHttpClientResponse, CancellationToken, ValueTask<TOutput>> Read,
        bool IsStreaming
    );

    private RequestStrategy<TInput> UnaryRequestStrategy<TInput>(Schema<TInput> inputSchema)
    {
        if (IsUnit<TInput>(inputSchema))
        {
            return new RequestStrategy<TInput>(
                static (_, _, _) => { },
                static (_, _) => default,
                IsStreaming: false
            );
        }

        var codec = codecFactory.FromSchema(
            inputSchema,
            new CodecFactoryOptions { MaterializeTopLevelDefaults = false }
        );
        return new RequestStrategy<TInput>(
            (request, input, _) =>
            {
                request.Body = new SmithyHttpBody.Bytes(codec.Serialize(input));
                request.ContentType = contentType;
            },
            (request, _) =>
            {
                var content = request.Body.BufferedContent;
                return content.Length == 0
                    ? default
                    : ValueTask.FromResult(codec.Deserialize(content));
            },
            IsStreaming: false
        );
    }

    private RequestStrategy<TInput> StreamingRequestStrategy<TInput, TBuilder, TEvent>(
        EventStreamBinding<TInput, TBuilder, TEvent> stream
    )
    {
        var codec = codecFactory.FromSchema(stream.EventSchema);
        var eventTypeOf = CompileEventType(stream.EventSchema);
        var binding = new EventStreamShapeBinding<TInput, TEvent, TBuilder>(
            stream,
            codecFactory,
            materializeTopLevelDefaults: false
        );
        return new RequestStrategy<TInput>(
            (request, input, cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(input);
                request.Body = new SmithyHttpBody.EventStreaming(
                    FrameShapeAsync(
                        input,
                        binding,
                        codec,
                        eventTypeOf,
                        InitialRequestEventType,
                        contentType,
                        cancellationToken
                    )
                );
                request.ContentType = EventStreamContentType;
            },
            (request, cancellationToken) =>
                ReadShapeAsync(
                    request.Body.OpenRead(),
                    disposeBody: false,
                    binding,
                    codec,
                    InitialRequestEventType,
                    contentType,
                    cancellationToken
                ),
            IsStreaming: true
        );
    }

    private ResponseStrategy<TOutput> UnaryResponseStrategy<TOutput>(Schema<TOutput> outputSchema)
    {
        // Two different questions. Nothing is written for any unit output, synthetic or not; but
        // only a literal SmithyUnit can be handed back without deserializing, since a synthetic
        // unit structure is its own CLR type and casting SmithyUnit to it would throw.
        var writesNoBody = IsUnit<TOutput>(outputSchema);
        var readsNoValue = typeof(TOutput) == typeof(SmithyUnit);
        var codec = codecFactory.FromSchema(outputSchema);

        return new ResponseStrategy<TOutput>(
            (output, _) =>
                writesNoBody
                    ? BufferedResponse(
                        200,
                        ReadOnlyMemory<byte>.Empty,
                        headers => headers["Smithy-Protocol"] = [protocolHeader]
                    )
                    : BufferedResponse(
                        200,
                        codec.Serialize(output),
                        headers =>
                        {
                            headers["Smithy-Protocol"] = [protocolHeader];
                            headers["Content-Type"] = [contentType];
                        }
                    ),
            (response, cancellationToken) =>
            {
                EnsureProtocolResponse(response);
                if (readsNoValue)
                {
                    response.Body.DisposeStream();
                    return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
                }

                return DeserializeSingleResponseAsync(response, codec, cancellationToken);
            },
            IsStreaming: false
        );
    }

    private ResponseStrategy<TOutput> StreamingResponseStrategy<TOutput, TBuilder, TEvent>(
        EventStreamBinding<TOutput, TBuilder, TEvent> stream
    )
    {
        var codec = codecFactory.FromSchema(stream.EventSchema);
        var eventTypeOf = CompileEventType(stream.EventSchema);
        var binding = new EventStreamShapeBinding<TOutput, TEvent, TBuilder>(
            stream,
            codecFactory,
            materializeTopLevelDefaults: true
        );
        return new ResponseStrategy<TOutput>(
            (output, cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(output);
                return StreamingResponse(
                    FrameShapeAsync(
                        output,
                        binding,
                        codec,
                        eventTypeOf,
                        InitialResponseEventType,
                        contentType,
                        cancellationToken
                    )
                );
            },
            (response, cancellationToken) =>
            {
                EnsureEventStreamResponse(response);
                return ReadShapeAsync(
                    response.Body.OpenRead(),
                    disposeBody: true,
                    binding,
                    codec,
                    InitialResponseEventType,
                    contentType,
                    cancellationToken
                );
            },
            IsStreaming: true
        );
    }

    private Func<TEvent, string> CompileEventType<TEvent>(Schema<TEvent> eventSchema) =>
        Schemas.CompileCaseName(
            eventSchema.Resolved as UnionSchema<TEvent>
                ?? throw new InvalidOperationException(
                    $"{protocolName} event streams must target a union schema."
                )
        );

    private sealed class OperationProtocol<TInput, TOutput>(
        RpcV2Protocol protocol,
        ServiceSchema service,
        OperationSchema<TInput, TOutput> operation,
        RequestStrategy<TInput> request,
        ResponseStrategy<TOutput> response,
        Action<SmithyHttpRequest>? requestTransform,
        ISmithyValidator<TInput>? inputValidator
    ) : IOperationProtocol<TInput, TOutput>
    {
        // The RPC v2 path is service-derived; the protocol owns this wire detail.
        private readonly string requestUri = RequestUri(service, operation);

        private readonly ModeledErrorSerializer serverErrors = ModeledErrorSerializer.Compile(
            operation.Errors,
            new ErrorWriter(protocol)
        );

        public IReadOnlyList<HttpOperationError> HttpErrors { get; } =
            HttpOperationError.Compile(operation.Errors, protocol.errorReader);

        public ISmithyValidator<TInput>? InputValidator { get; } = inputValidator;

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            // Accept advertises what comes back, so it follows the response direction, not the
            // request's.
            var message = protocol.BaseRequest(
                requestUri,
                response.IsStreaming ? EventStreamContentType : protocol.contentType
            );
            message.ExpectStreamingResponse = response.IsStreaming;
            request.Write(message, input, cancellationToken);

            // @requestCompression and @httpChecksumRequired both rewrite a buffered body, so they
            // have nothing to act on once the request is a live event stream.
            if (!request.IsStreaming)
            {
                requestTransform?.Invoke(message);
            }

            return message;
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest message,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(message);

            // RPC v2 routes by path alone; a target header is the awsJson way of naming the
            // operation, and the spec requires a server to reject a request that carries one.
            if (
                message.Headers.ContainsKey("X-Amz-Target")
                || message.Headers.ContainsKey("X-Amzn-Target")
            )
            {
                throw MalformedRequestException.Serialization(
                    $"{protocol.protocolName} requests must not carry an X-Amz-Target or X-Amzn-Target header."
                );
            }

            return request.Read(message, cancellationToken);
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        ) => response.Write(output, cancellationToken);

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse message,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(message);
            return response.Read(message, cancellationToken);
        }

        public bool IsErrorResponse(SmithyHttpClientResponse message) =>
            (int)message.StatusCode >= 400;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse message,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                OperationProtocolErrors.DeserializeModeledError(
                    HttpErrors,
                    message,
                    r => protocol.IsProtocolResponse(r) ? protocol.ReadErrorType(r.Content) : null,
                    requiresErrorDiscriminator: true,
                    supportsHttpStatusErrorFallback: false
                )
            );

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse message) =>
            serverErrors.TrySerialize(exception, out message);
    }

    /// <summary>
    /// Serializes a modeled error into an error response: a body carrying a <c>__type</c>
    /// discriminator (the absolute shape id) plus the error's members, with the supplied HTTP
    /// status code and the protocol header.
    /// </summary>
    /// <remarks>
    /// This compiles the error's writer on every call, so it is the ad-hoc entry point; the server
    /// path compiles each error once instead.
    /// </remarks>
    protected SmithyHttpServerResponse SerializeErrorResponse<TError>(
        Schema<TError> errorSchema,
        TError error,
        string errorShapeId,
        int statusCode
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(errorShapeId);
        return ErrorResponse(CompileErrorWriter(errorSchema, errorShapeId)(error), statusCode);
    }

    private Func<TError, byte[]> CompileErrorWriter<TError>(
        Schema<TError> errorSchema,
        string errorShapeId
    ) =>
        errorSchema.Resolved is IStructSchema<TError> structSchema
            ? CompileErrorBody(structSchema, errorShapeId)
            : throw new InvalidOperationException(
                $"{protocolName} errors must be backed by a structure schema."
            );

    private SmithyHttpServerResponse ErrorResponse(byte[] body, int statusCode) =>
        BufferedResponse(
            statusCode,
            body,
            headers =>
            {
                headers["Smithy-Protocol"] = [protocolHeader];
                headers["Content-Type"] = [contentType];
            }
        );

    private sealed class ErrorWriter(RpcV2Protocol protocol) : IErrorWriterCompiler
    {
        public Func<TError, SmithyHttpServerResponse> Compile<TError>(
            OperationErrorSchema<TError> schema
        )
            where TError : Exception
        {
            var writeBody = protocol.CompileErrorWriter(schema.Schema, schema.Id.ToString());
            var statusCode = schema.HttpStatusCode;
            return value => protocol.ErrorResponse(writeBody(value), statusCode);
        }
    }

    private static SmithyHttpServerResponse BufferedResponse(
        int statusCode,
        ReadOnlyMemory<byte> body,
        Action<IDictionary<string, IReadOnlyList<string>>>? headers = null
    )
    {
        var response = new SmithyHttpServerResponse
        {
            StatusCode = statusCode,
            Body = SingleChunk(body),
            ContentLength = body.Length,
        };
        headers?.Invoke(response.Headers);
        return response;
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> SingleChunk(
        ReadOnlyMemory<byte> chunk
    )
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return chunk;
    }

    private SmithyHttpRequest BaseRequest(string requestUri, string accept)
    {
        var request = new SmithyHttpRequest(HttpMethod.Post, requestUri);
        request.Headers["Smithy-Protocol"] = [protocolHeader];
        request.Headers["Accept"] = [accept];
        return request;
    }

    private SmithyHttpServerResponse StreamingResponse(IAsyncEnumerable<ReadOnlyMemory<byte>> body)
    {
        var response = new SmithyHttpServerResponse { StatusCode = 200, Body = body };
        response.Headers["Smithy-Protocol"] = [protocolHeader];
        response.Headers["Content-Type"] = [EventStreamContentType];
        return response;
    }

    private static string RequestUri<TInput, TOutput>(
        ServiceSchema service,
        OperationSchema<TInput, TOutput> operation
    ) => $"/service/{service.Id.Name}/operation/{operation.Id.Name}";

    /// <summary>
    /// An event-stream shape as RPC v2 frames it: the stream's members other than the events
    /// travel in an initial message ahead of them.
    /// </summary>
    private sealed class EventStreamShapeBinding<TShape, TEvent, TBuilder>(
        EventStreamBinding<TShape, TBuilder, TEvent> stream,
        IProjectionCodecFactory codecFactory,
        bool materializeTopLevelDefaults
    )
    {
        public IProjectionCodec<TShape, TBuilder> InitialCodec { get; } =
            codecFactory.FromProjection(
                stream.InitialMembers,
                new CodecFactoryOptions
                {
                    MaterializeTopLevelDefaults = materializeTopLevelDefaults,
                }
            );

        public bool HasInitialMembers => stream.HasInitialMembers;

        public IAsyncEnumerable<TEvent> GetEvents(TShape shape) => stream.GetEvents(shape);

        public TShape Build(byte[] initialPayload, IAsyncEnumerable<TEvent> events) =>
            stream.Build(
                events,
                initialPayload.Length > 0
                    ? builder => InitialCodec.ReadInto(initialPayload, builder)
                    : null
            );
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> FrameShapeAsync<
        TShape,
        TEvent,
        TBuilder
    >(
        TShape shape,
        EventStreamShapeBinding<TShape, TEvent, TBuilder> binding,
        ICodec<TEvent> eventCodec,
        Func<TEvent, string> eventTypeOf,
        string initialEventType,
        string payloadContentType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (binding.HasInitialMembers)
        {
            yield return EventStreamEvents
                .Create(initialEventType, binding.InitialCodec.Serialize(shape), payloadContentType)
                .Encode();
        }

        await foreach (
            var chunk in EventStreamEvents
                .EncodeAsync(
                    binding.GetEvents(shape),
                    eventTypeOf,
                    eventCodec.Serialize,
                    payloadContentType,
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            yield return chunk;
        }
    }

    private static async ValueTask<TShape> ReadShapeAsync<TShape, TEvent, TBuilder>(
        Stream body,
        bool disposeBody,
        EventStreamShapeBinding<TShape, TEvent, TBuilder> binding,
        ICodec<TEvent> eventCodec,
        string initialEventType,
        string payloadContentType,
        CancellationToken cancellationToken
    )
    {
        var enumerator = EventStreamMessageReader
            .ReadAllAsync(body, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        EventStreamMessage? firstEvent = null;
        var initialPayload = ReadOnlyMemory<byte>.Empty;
        var ownsEnumerator = true;

        try
        {
            if (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                var first = enumerator.Current;
                EventStreamEvents.EnsureEvent(first);
                var eventType = first.StringHeader(EventStreamHeaders.EventType);
                if (string.Equals(eventType, initialEventType, StringComparison.Ordinal))
                {
                    initialPayload = EventStreamEvents.ReadPayload(first, payloadContentType);
                }
                else
                {
                    firstEvent = first;
                }
            }

            var shape = binding.Build(
                initialPayload.ToArray(),
                ReadRemainingEventsAsync(
                    firstEvent,
                    enumerator,
                    body,
                    disposeBody,
                    eventCodec,
                    payloadContentType,
                    cancellationToken
                )
            );
            ownsEnumerator = false;
            return shape;
        }
        finally
        {
            if (ownsEnumerator)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
                if (disposeBody)
                {
                    await body.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        static async IAsyncEnumerable<TEvent> ReadRemainingEventsAsync(
            EventStreamMessage? first,
            IAsyncEnumerator<EventStreamMessage> enumerator,
            Stream body,
            bool disposeBody,
            ICodec<TEvent> eventCodec,
            string payloadContentType,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await using (enumerator.ConfigureAwait(false))
            {
                try
                {
                    if (first is not null)
                    {
                        var firstValue = DeserializeEventMessage(
                            eventCodec,
                            first,
                            payloadContentType
                        );
                        if (firstValue is not null)
                        {
                            yield return firstValue;
                        }
                    }

                    while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        var value = DeserializeEventMessage(
                            eventCodec,
                            enumerator.Current,
                            payloadContentType
                        );
                        if (value is not null)
                        {
                            yield return value;
                        }
                    }
                }
                finally
                {
                    if (disposeBody)
                    {
                        await body.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private static async ValueTask<T> DeserializeSingleResponseAsync<T>(
        SmithyHttpClientResponse response,
        ICodec<T> codec,
        CancellationToken cancellationToken
    )
    {
        var body = response.Body.OpenRead();
        await using (body.ConfigureAwait(false))
        {
            using var stream = new MemoryStream();
            await body.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            var content = stream.ToArray();
            return content.Length == 0 ? default! : codec.Deserialize(content);
        }
    }

    private static T? DeserializeEventMessage<T>(
        ICodec<T> codec,
        EventStreamMessage message,
        string payloadContentType
    ) => codec.Deserialize(EventStreamEvents.ReadPayload(message, payloadContentType).ToArray());

    private void EnsureEventStreamResponse(SmithyHttpClientResponse response)
    {
        EnsureProtocolResponse(response);
        if (response.StatusCode == HttpStatusCode.OK && IsEventStreamContentType(response))
        {
            return;
        }

        response.Body.DisposeStream();
        throw new InvalidOperationException(
            $"Expected a {protocolName} event stream response but received HTTP {(int)response.StatusCode}."
        );
    }

    private static bool IsEventStreamContentType(SmithyHttpClientResponse response) =>
        response.ContentHeaders.TryGetValue("Content-Type", out var contentType)
        && contentType.Any(value =>
            value.StartsWith(EventStreamContentType, StringComparison.OrdinalIgnoreCase)
        );

    private static bool IsUnit<T>(Schema schema) =>
        typeof(T) == typeof(SmithyUnit) || Schemas.IsSyntheticUnit(schema);

    private static byte[] RequiredBody(byte[] content) =>
        content.Length == 0
            ? throw new InvalidOperationException("Response body is required but was empty.")
            : content;

    /// <summary>Whether the response carries this protocol's <c>Smithy-Protocol</c> header.</summary>
    protected bool IsProtocolResponse(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValue("Smithy-Protocol", out var values)
            && values.Any(value => string.Equals(value, protocolHeader, StringComparison.Ordinal));
    }

    /// <summary>Throws unless the response carries this protocol's <c>Smithy-Protocol</c> header.</summary>
    protected void EnsureProtocolResponse(SmithyHttpClientResponse response)
    {
        if (!IsProtocolResponse(response))
        {
            throw new InvalidOperationException(
                $"{protocolName} response is missing the required Smithy-Protocol header."
            );
        }
    }
}
