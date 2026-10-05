using System.Formats.Cbor;
using System.Net;
using System.Runtime.CompilerServices;
using NSmithy.Codecs.Cbor;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.EventStream;
using NSmithy.Http;

namespace NSmithy.Protocols.RpcV2Cbor;

public sealed class RpcV2CborProtocol : IProtocol
{
    private static readonly CborCodecFactory CodecFactory = CborCodecFactory.Default;

    private const string ContentType = "application/cbor";
    private const string EventStreamContentType = "application/vnd.amazon.eventstream";
    private const string InitialRequestEventType = "initial-request";
    private const string InitialResponseEventType = "initial-response";

    /// <summary>
    /// Binds the protocol to a service, yielding per-operation protocols. The service schema
    /// supplies the service shape name used to derive each operation's request path.
    /// </summary>
    public IServiceProtocol ForService(ServiceSchema service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new ServiceProtocol(service);
    }

    private sealed class ServiceProtocol(ServiceSchema service) : IServiceProtocol
    {
        public IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        ) => CreateOperation(operation, validateInput: false);

        public IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        ) => CreateOperation(operation, validateInput: true);

        private OperationProtocol<TInput, TOutput> CreateOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation,
            bool validateInput
        )
        {
            ArgumentNullException.ThrowIfNull(operation);

            // Decided once here rather than per shape: an event stream changes how the body is
            // framed, not what the operation's input structure has to satisfy. The events
            // themselves are not covered, since the validator skips the event-stream member and
            // rejecting one mid-stream needs a way to report it after the response has begun.
            var inputValidator = validateInput ? SmithyValidator.FromSchema(operation.Input) : null;

            // Each direction is chosen independently; the four call shapes are their cross product.
            var request = EventStreamBinding.TryBind(
                operation.Input,
                new StreamingRequestCompiler<TInput>(),
                out var streamingRequest
            )
                ? streamingRequest
                : UnaryRequestStrategy(operation.Input);
            var response = EventStreamBinding.TryBind(
                operation.Output,
                new StreamingResponseCompiler<TOutput>(),
                out var streamingResponse
            )
                ? streamingResponse
                : UnaryResponseStrategy(operation.Output);

            return new OperationProtocol<TInput, TOutput>(
                service,
                operation,
                request,
                response,
                SmithyRequestModifiers.Compile(operation),
                inputValidator
            );
        }
    }

    private sealed class StreamingRequestCompiler<TInput>
        : IEventStreamBindingVisitor<TInput, RequestStrategy<TInput>>
    {
        public RequestStrategy<TInput> Visit<TBuilder, TEvent>(
            EventStreamBinding<TInput, TBuilder, TEvent> binding
        ) => StreamingRequestStrategy(binding);
    }

    private sealed class StreamingResponseCompiler<TOutput>
        : IEventStreamBindingVisitor<TOutput, ResponseStrategy<TOutput>>
    {
        public ResponseStrategy<TOutput> Visit<TBuilder, TEvent>(
            EventStreamBinding<TOutput, TBuilder, TEvent> binding
        ) => StreamingResponseStrategy(binding);
    }

    /// <summary>
    /// How one direction of an operation is put on and taken off the wire. Unlike gRPC, the two
    /// framings genuinely differ: a unary body is bare CBOR at <c>application/cbor</c>, while a
    /// stream is eventstream-framed, prefixed by an initial-request or initial-response message
    /// carrying the non-stream members. Resolving that once at bind time into these delegates is
    /// what lets a single operation protocol serve all four call shapes.
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

    private static RequestStrategy<TInput> UnaryRequestStrategy<TInput>(Schema<TInput> inputSchema)
    {
        if (IsUnit<TInput>(inputSchema))
        {
            return new RequestStrategy<TInput>(
                static (_, _, _) => { },
                static (_, _) => default,
                IsStreaming: false
            );
        }

        var codec = CodecFactory.FromSchema(
            inputSchema,
            new CodecFactoryOptions { MaterializeTopLevelDefaults = false }
        );
        return new RequestStrategy<TInput>(
            (request, input, _) =>
            {
                request.Body = new SmithyHttpBody.Bytes(codec.Serialize(input));
                request.ContentType = ContentType;
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

    private static RequestStrategy<TInput> StreamingRequestStrategy<TInput, TBuilder, TEvent>(
        EventStreamBinding<TInput, TBuilder, TEvent> stream
    )
    {
        var codec = CodecFactory.FromSchema(stream.EventSchema);
        var eventTypeOf = CompileEventType(stream.EventSchema);
        var binding = new EventStreamShapeBinding<TInput, TEvent, TBuilder>(
            stream,
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
                    cancellationToken
                ),
            IsStreaming: true
        );
    }

    private static ResponseStrategy<TOutput> UnaryResponseStrategy<TOutput>(
        Schema<TOutput> outputSchema
    )
    {
        // Two different questions. Nothing is written for any unit output, synthetic or not; but
        // only a literal SmithyUnit can be handed back without deserializing, since a synthetic
        // unit structure is its own CLR type and casting SmithyUnit to it would throw.
        var writesNoBody = IsUnit<TOutput>(outputSchema);
        var readsNoValue = typeof(TOutput) == typeof(SmithyUnit);
        var codec = CodecFactory.FromSchema(outputSchema);

        return new ResponseStrategy<TOutput>(
            (output, _) =>
                writesNoBody
                    ? BufferedResponse(
                        200,
                        ReadOnlyMemory<byte>.Empty,
                        headers => headers["Smithy-Protocol"] = ["rpc-v2-cbor"]
                    )
                    : BufferedResponse(
                        200,
                        codec.Serialize(output),
                        headers =>
                        {
                            headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                            headers["Content-Type"] = [ContentType];
                        }
                    ),
            (response, cancellationToken) =>
            {
                EnsureResponse(response);
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

    private static ResponseStrategy<TOutput> StreamingResponseStrategy<TOutput, TBuilder, TEvent>(
        EventStreamBinding<TOutput, TBuilder, TEvent> stream
    )
    {
        var codec = CodecFactory.FromSchema(stream.EventSchema);
        var eventTypeOf = CompileEventType(stream.EventSchema);
        var binding = new EventStreamShapeBinding<TOutput, TEvent, TBuilder>(
            stream,
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
                    cancellationToken
                );
            },
            IsStreaming: true
        );
    }

    private static Func<TEvent, string> CompileEventType<TEvent>(Schema<TEvent> eventSchema) =>
        Schemas.CompileCaseName(
            eventSchema.Resolved as IUnionSchema<TEvent>
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor event streams must target a union schema."
                )
        );

    private sealed class OperationProtocol<TInput, TOutput>(
        ServiceSchema service,
        OperationSchema<TInput, TOutput> operation,
        RequestStrategy<TInput> request,
        ResponseStrategy<TOutput> response,
        Action<SmithyHttpRequest>? requestTransform,
        ISmithyValidator<TInput>? inputValidator
    ) : IOperationProtocol<TInput, TOutput>
    {
        // The rpcv2Cbor path is service-derived; the protocol owns this wire detail.
        private readonly string requestUri = RequestUri(service, operation);

        private readonly ModeledErrorSerializer serverErrors = ModeledErrorSerializer.Compile(
            operation.Errors,
            ErrorWriter.Instance
        );

        public IReadOnlyList<HttpOperationError> HttpErrors { get; } =
            HttpOperationError.Compile(operation.Errors, ErrorReader);

        public ISmithyValidator<TInput>? InputValidator { get; } = inputValidator;

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            // Accept advertises what comes back, so it follows the response direction, not the
            // request's.
            var message = BaseRequest(
                requestUri,
                response.IsStreaming ? EventStreamContentType : ContentType
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
        ) => DeserializeModeledErrorAsync(HttpErrors, message);

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse message) =>
            serverErrors.TrySerialize(exception, out message);
    }

    /// <summary>
    /// Serializes a modeled error into a rpcv2Cbor error response: a CBOR body carrying a
    /// <c>__type</c> discriminator (the absolute shape id) plus the error's members, with the
    /// supplied HTTP status code and the protocol header.
    /// </summary>
    public static SmithyHttpServerResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError error,
        string errorShapeId,
        int statusCode
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(errorShapeId);

        return SerializeError(
            CompileErrorMemberWriters(errorSchema),
            error,
            errorShapeId,
            statusCode
        );
    }

    private static SmithyHttpServerResponse SerializeError<TError>(
        CborStructMembersWriter<TError> memberWriters,
        TError error,
        string errorShapeId,
        int statusCode
    ) =>
        BufferedResponse(
            statusCode,
            SerializeErrorBody(memberWriters, error, errorShapeId),
            headers =>
            {
                headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                headers["Content-Type"] = [ContentType];
            }
        );

    /// <summary>
    /// Compiles an error shape's member writers, which is everything the shape determines about how
    /// its body is written.
    /// </summary>
    /// <remarks>
    /// <see cref="SerializeError"/> compiles on every call, so it is the ad-hoc entry point; the
    /// server path holds the result of this per error instead. Compiling per response also discarded
    /// the <c>SchemaCompilationCache</c> that the fresh <c>CborWriterCompiler</c> carries, so a
    /// shape referenced twice was compiled twice, every time.
    /// </remarks>
    internal static CborStructMembersWriter<TError> CompileErrorMemberWriters<TError>(
        Schema<TError> errorSchema
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);

        if (errorSchema.Resolved is not IStructSchema<TError> structSchema)
        {
            throw new InvalidOperationException(
                "rpcv2Cbor errors must be backed by a structure schema."
            );
        }

        return new CborWriterCompiler().CompileMembers(structSchema, materializeDefaults: true);
    }

    private static byte[] SerializeErrorBody<TError>(
        CborStructMembersWriter<TError> memberWriters,
        TError error,
        string errorShapeId
    )
    {
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(null);
        writer.WriteTextString("__type");
        writer.WriteTextString(errorShapeId);
        memberWriters.Write(writer, error);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static ValueTask<Exception?> DeserializeModeledErrorAsync(
        IReadOnlyList<HttpOperationError> httpErrors,
        SmithyHttpClientResponse response
    ) =>
        ValueTask.FromResult(
            OperationProtocolErrors.DeserializeModeledError(
                httpErrors,
                response,
                r => HasResponse(r) ? DeserializeErrorType(r) : null,
                requiresErrorDiscriminator: true,
                supportsHttpStatusErrorFallback: false
            )
        );

    private static readonly CodecErrorReader ErrorReader = new(
        CodecFactory,
        response => RequiredBody(response.Content)
    );

    private sealed class ErrorWriter : IErrorWriterCompiler
    {
        public static ErrorWriter Instance { get; } = new();

        public Func<TError, SmithyHttpServerResponse> Compile<TError>(
            OperationErrorSchema<TError> schema
        )
            where TError : Exception
        {
            var memberWriters = CompileErrorMemberWriters(schema.Schema);
            var errorShapeId = schema.Id.ToString();
            var statusCode = schema.HttpStatusCode;
            return value => SerializeError(memberWriters, value, errorShapeId, statusCode);
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

    private static SmithyHttpRequest BaseRequest(string requestUri, string accept)
    {
        var request = new SmithyHttpRequest(HttpMethod.Post, requestUri);
        request.Headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
        request.Headers["Accept"] = [accept];
        return request;
    }

    private static SmithyHttpServerResponse StreamingResponse(
        IAsyncEnumerable<ReadOnlyMemory<byte>> body
    )
    {
        var response = new SmithyHttpServerResponse { StatusCode = 200, Body = body };
        response.Headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
        response.Headers["Content-Type"] = [EventStreamContentType];
        return response;
    }

    private static string RequestUri<TInput, TOutput>(
        ServiceSchema service,
        OperationSchema<TInput, TOutput> operation
    ) => $"/service/{service.Id.Name}/operation/{operation.Id.Name}";

    /// <summary>
    /// An event-stream shape as rpcv2Cbor frames it: the stream's members other than the events
    /// travel in an initial message ahead of them.
    /// </summary>
    private sealed class EventStreamShapeBinding<TShape, TEvent, TBuilder>(
        EventStreamBinding<TShape, TBuilder, TEvent> stream,
        bool materializeTopLevelDefaults
    )
    {
        public IProjectionCodec<TShape, TBuilder> InitialCodec { get; } =
            CodecFactory.FromProjection(
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (binding.HasInitialMembers)
        {
            yield return EventStreamEvents
                .Create(initialEventType, binding.InitialCodec.Serialize(shape), ContentType)
                .Encode();
        }

        await foreach (
            var chunk in EventStreamEvents
                .EncodeAsync(
                    binding.GetEvents(shape),
                    eventTypeOf,
                    eventCodec.Serialize,
                    ContentType,
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
                    initialPayload = EventStreamEvents.ReadPayload(first, ContentType);
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
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await using (enumerator.ConfigureAwait(false))
            {
                try
                {
                    if (first is not null)
                    {
                        var firstValue = DeserializeEventMessage(eventCodec, first);
                        if (firstValue is not null)
                        {
                            yield return firstValue;
                        }
                    }

                    while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        var value = DeserializeEventMessage(eventCodec, enumerator.Current);
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

    private static T? DeserializeEventMessage<T>(ICodec<T> codec, EventStreamMessage message) =>
        codec.Deserialize(EventStreamEvents.ReadPayload(message, ContentType).ToArray());

    private static void EnsureEventStreamResponse(SmithyHttpClientResponse response)
    {
        EnsureResponse(response);
        if (response.StatusCode == HttpStatusCode.OK && IsEventStreamContentType(response))
        {
            return;
        }

        response.Body.DisposeStream();
        throw new InvalidOperationException(
            $"Expected a rpcv2Cbor event stream response but received HTTP {(int)response.StatusCode}."
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

    public static bool HasResponse(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValue("Smithy-Protocol", out var values)
            && values.Any(value => string.Equals(value, "rpc-v2-cbor", StringComparison.Ordinal));
    }

    public static void EnsureResponse(SmithyHttpClientResponse response)
    {
        if (!HasResponse(response))
        {
            throw new InvalidOperationException(
                "rpcv2Cbor response is missing the required Smithy-Protocol header."
            );
        }
    }

    public static string? DeserializeErrorType(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return DeserializeErrorType(response.Content);
    }

    public static string? DeserializeErrorType(byte[] content)
    {
        if (content.Length == 0)
            return null;
        try
        {
            var reader = new System.Formats.Cbor.CborReader(
                content,
                System.Formats.Cbor.CborConformanceMode.Lax
            );
            if (reader.PeekState() != System.Formats.Cbor.CborReaderState.StartMap)
                return null;
            reader.ReadStartMap();
            while (reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                if (string.Equals(key, "__type", StringComparison.Ordinal))
                    return reader.ReadTextString();
                reader.SkipValue();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
