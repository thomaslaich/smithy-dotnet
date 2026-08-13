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
            var inputEvent = FindEventStreamEventSchema(operation.Input);
            var outputEvent = FindEventStreamEventSchema(operation.Output);
            RequestStrategy<TInput> request = inputEvent is null
                ? UnaryRequestStrategy(operation.Input)
                : StreamingRequestStrategy(RequireStruct(operation.Input), (dynamic)inputEvent);
            ResponseStrategy<TOutput> response = outputEvent is null
                ? UnaryResponseStrategy(operation.Output)
                : StreamingResponseStrategy(RequireStruct(operation.Output), (dynamic)outputEvent);

            return new OperationProtocol<TInput, TOutput>(
                service,
                operation,
                request,
                response,
                SmithyRequestModifiers.Compile(operation),
                inputValidator
            );
        }

        /// <summary>
        /// An event-stream shape is always a structure: the stream is one member of it, alongside
        /// whatever the initial message carries. The builder type comes back erased, so the
        /// strategy that captures it is reached through dynamic dispatch.
        /// </summary>
        private static dynamic RequireStruct<T>(Schema<T> schema) =>
            schema.Resolved as IStructSchema<T>
            ?? throw new InvalidOperationException(
                $"rpcv2Cbor event stream shape '{schema.Id}' must use a structure shape."
            );
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

        var codec = CborCodec.FromSchema(inputSchema, materializeTopLevelDefaults: false);
        return new RequestStrategy<TInput>(
            (request, input, _) =>
            {
                request.Body = new SmithyHttpBody.Bytes(codec.Serialize(input));
                request.ContentType = ContentType;
            },
            (request, _) =>
            {
                var content = BodyBytes(request.Body);
                return content.Length == 0
                    ? default
                    : ValueTask.FromResult(codec.Deserialize(content));
            },
            IsStreaming: false
        );
    }

    private static RequestStrategy<TInput> StreamingRequestStrategy<
        TInput,
        TInputEvent,
        TInputBuilder
    >(IStructSchema<TInput, TInputBuilder> inputSchema, Schema<TInputEvent> eventSchema)
        where TInputBuilder : notnull
    {
        var codec = CborCodec.FromSchema(eventSchema);
        var union = RequireUnion(eventSchema);
        var binding = EventStreamShapeBinding<TInput, TInputEvent, TInputBuilder>.Create(
            inputSchema,
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
                        union,
                        InitialRequestEventType,
                        cancellationToken
                    )
                );
                request.ContentType = EventStreamContentType;
            },
            (request, cancellationToken) =>
                ReadShapeAsync(
                    RequestStream(request),
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
        var codec = CborCodec.FromSchema(outputSchema);

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
                    DisposeResponseBody(response);
                    return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
                }

                return DeserializeSingleResponseAsync(response, codec, cancellationToken);
            },
            IsStreaming: false
        );
    }

    private static ResponseStrategy<TOutput> StreamingResponseStrategy<
        TOutput,
        TOutputEvent,
        TOutputBuilder
    >(IStructSchema<TOutput, TOutputBuilder> outputSchema, Schema<TOutputEvent> eventSchema)
        where TOutputBuilder : notnull
    {
        var codec = CborCodec.FromSchema(eventSchema);
        var union = RequireUnion(eventSchema);
        var binding = EventStreamShapeBinding<TOutput, TOutputEvent, TOutputBuilder>.Create(
            outputSchema,
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
                        union,
                        InitialResponseEventType,
                        cancellationToken
                    )
                );
            },
            (response, cancellationToken) =>
            {
                EnsureEventStreamResponse(response);
                return ReadShapeAsync(
                    ResponseStream(response),
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

    private static IUnionSchema RequireUnion<TEvent>(Schema<TEvent> eventSchema) =>
        eventSchema.Resolved as IUnionSchema
        ?? throw new InvalidOperationException(
            "rpcv2Cbor event streams must target a union schema."
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
            error => CompileServerError((dynamic)error)
        );

        public IReadOnlyList<HttpOperationError> HttpErrors { get; } =
            CompileErrors(operation.Errors);

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

        return BufferedResponse(
            statusCode,
            SerializeErrorBody(errorSchema, error, errorShapeId),
            headers =>
            {
                headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                headers["Content-Type"] = [ContentType];
            }
        );
    }

    private static byte[] SerializeErrorBody<TError>(
        Schema<TError> errorSchema,
        TError error,
        string errorShapeId
    )
    {
        if (errorSchema.Resolved is not IStructSchema<TError> structSchema)
        {
            throw new InvalidOperationException(
                "rpcv2Cbor errors must be backed by a structure schema."
            );
        }

        var visitor = new CborMemberWriterCompiler<TError>(
            new CborWriterCompiler(),
            materializeDefaults: true
        );
        structSchema.VisitMembers(visitor);

        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(null);
        writer.WriteTextString("__type");
        writer.WriteTextString(errorShapeId);
        foreach (var memberWriter in visitor.Writers)
        {
            memberWriter.Write(writer, error);
        }

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

    private static (Type, Func<Exception, SmithyHttpServerResponse>) CompileServerError<TError>(
        OperationErrorSchema<TError> error
    )
        where TError : Exception =>
        (
            typeof(TError),
            exception =>
                SerializeError(
                    error.Schema,
                    (TError)exception,
                    error.Id.ToString(),
                    error.HttpStatusCode
                )
        );

    private static HttpOperationError[] CompileErrors(
        IReadOnlyList<IOperationErrorSchema> errors
    ) => errors.Select(error => (HttpOperationError)CompileError((dynamic)error)).ToArray();

    private static HttpOperationError CompileError<TError>(OperationErrorSchema<TError> error)
        where TError : Exception
    {
        var codec = CborCodec.FromSchema(error.Schema);
        return new HttpOperationError(
            error.Id,
            error.HttpStatusCode,
            response => DeserializeRequiredBody(codec, response.Content)
        );
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

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> FrameEventsAsync<T>(
        IAsyncEnumerable<T> events,
        ICborCodec<T> codec,
        IUnionSchema eventSchema,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var value in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            var eventType = eventSchema.GetCaseObject(value!).Name;
            yield return CreateEventStreamMessage(eventType, codec.Serialize(value)).Encode();
        }
    }

    private sealed class EventStreamShapeBinding<TShape, TEvent, TBuilder>
        where TBuilder : notnull
    {
        private EventStreamShapeBinding(
            IStructSchema<TShape, TBuilder> structure,
            IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> streamMember,
            IProjectionCodec<TShape, TBuilder> initialCodec,
            bool hasInitialMembers
        )
        {
            Structure = structure;
            StreamMember = streamMember;
            InitialCodec = initialCodec;
            HasInitialMembers = hasInitialMembers;
        }

        public IStructSchema<TShape, TBuilder> Structure { get; }

        public IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> StreamMember { get; }

        public IProjectionCodec<TShape, TBuilder> InitialCodec { get; }

        public bool HasInitialMembers { get; }

        public static EventStreamShapeBinding<TShape, TEvent, TBuilder> Create(
            IStructSchema<TShape, TBuilder> structure,
            bool materializeTopLevelDefaults
        )
        {
            var streamMember = FindStreamMember(structure);
            var initialMembers = CollectInitialMembers(structure, streamMember);
            return new EventStreamShapeBinding<TShape, TEvent, TBuilder>(
                structure,
                streamMember,
                CborCodec.FromProjection(
                    Schemas.Project(structure, initialMembers),
                    materializeTopLevelDefaults
                ),
                initialMembers.Length > 0
            );
        }

        public IAsyncEnumerable<TEvent> GetEvents(TShape shape) =>
            StreamMember.GetValue(shape)
            ?? throw new InvalidOperationException(
                $"Event stream member '{StreamMember.Name}' was null."
            );

        public TShape Build(byte[] initialPayload, IAsyncEnumerable<TEvent> events)
        {
            var builder = Structure.CreateTypedBuilder();
            if (initialPayload.Length > 0)
            {
                InitialCodec.ReadInto(initialPayload, builder);
            }

            StreamMember.SetValue(builder, events);
            return Structure.Build(builder);
        }

        private static IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> FindStreamMember(
            IStructSchema<TShape, TBuilder> structure
        )
        {
            var visitor = new EventStreamMemberVisitor<TShape, TBuilder, TEvent>();
            structure.VisitMembers(visitor);
            return visitor.Member
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor initial event streams require one event stream member."
                );
        }

        private static IMemberSchema<TShape>[] CollectInitialMembers(
            IStructSchema<TShape, TBuilder> structure,
            IMemberSchema<TShape> streamMember
        ) =>
            Schemas
                .GetMembers(structure)
                .Where(member => !ReferenceEquals(member, streamMember))
                .ToArray();
    }

    private sealed class EventStreamMemberVisitor<TShape, TBuilder, TEvent>
        : IMemberVisitor<TShape, TBuilder>
    {
        public IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>>? Member
        {
            get;
            private set;
        }

        public void Visit<TValue>(IMemberSchema<TShape, TBuilder, TValue> member)
        {
            if (member.Target is not EventStreamSchema<TEvent>)
            {
                return;
            }

            if (Member is not null)
            {
                throw new InvalidOperationException(
                    "rpcv2Cbor initial event streams require one event stream member."
                );
            }

            Member = (IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>>)(object)member;
        }
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> FrameShapeAsync<
        TShape,
        TEvent,
        TBuilder
    >(
        TShape shape,
        EventStreamShapeBinding<TShape, TEvent, TBuilder> binding,
        ICborCodec<TEvent> eventCodec,
        IUnionSchema eventSchema,
        string initialEventType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
        where TBuilder : notnull
    {
        if (binding.HasInitialMembers)
        {
            yield return CreateEventStreamMessage(
                    initialEventType,
                    binding.InitialCodec.Serialize(shape)
                )
                .Encode();
        }

        await foreach (
            var chunk in FrameEventsAsync(
                    binding.GetEvents(shape),
                    eventCodec,
                    eventSchema,
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            yield return chunk;
        }
    }

    private static EventStreamMessage CreateEventStreamMessage(
        string eventType,
        ReadOnlyMemory<byte> payload
    ) =>
        new(
            new Dictionary<string, EventStreamHeaderValue>
            {
                [EventStreamHeaders.MessageType] = new EventStreamHeaderValue.Text(
                    EventStreamHeaders.EventMessageType
                ),
                [EventStreamHeaders.EventType] = new EventStreamHeaderValue.Text(eventType),
                [EventStreamHeaders.ContentType] = new EventStreamHeaderValue.Text(ContentType),
            },
            payload
        );

    private static async ValueTask<TShape> ReadShapeAsync<TShape, TEvent, TBuilder>(
        Stream body,
        bool disposeBody,
        EventStreamShapeBinding<TShape, TEvent, TBuilder> binding,
        ICborCodec<TEvent> eventCodec,
        string initialEventType,
        CancellationToken cancellationToken
    )
        where TBuilder : notnull
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
                EnsureEventMessage(first);
                var eventType = first.StringHeader(EventStreamHeaders.EventType);
                if (string.Equals(eventType, initialEventType, StringComparison.Ordinal))
                {
                    EnsureCborPayload(first);
                    initialPayload = first.Payload;
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
            ICborCodec<TEvent> eventCodec,
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
        ICborCodec<T> codec,
        CancellationToken cancellationToken
    )
    {
        var body = ResponseStream(response);
        await using (body.ConfigureAwait(false))
        {
            using var stream = new MemoryStream();
            await body.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            var content = stream.ToArray();
            return content.Length == 0 ? default! : codec.Deserialize(content);
        }
    }

    private static T? DeserializeEventMessage<T>(ICborCodec<T> codec, EventStreamMessage message)
    {
        EnsureEventMessage(message);
        EnsureCborPayload(message);
        return codec.Deserialize(message.Payload.ToArray());
    }

    private static void EnsureEventMessage(EventStreamMessage message)
    {
        var messageType = message.StringHeader(EventStreamHeaders.MessageType);
        if (
            !string.Equals(
                messageType,
                EventStreamHeaders.EventMessageType,
                StringComparison.Ordinal
            )
        )
        {
            ThrowEventStreamException(message);
        }
    }

    private static void EnsureCborPayload(EventStreamMessage message)
    {
        var contentType = message.StringHeader(EventStreamHeaders.ContentType);
        if (
            contentType is not null
            && !string.Equals(contentType, ContentType, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException(
                $"Expected rpcv2Cbor event payload content type '{ContentType}' but received '{contentType}'."
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
            throw new InvalidOperationException($"rpcv2Cbor event stream exception: {type}.");
        }

        throw new InvalidDataException(
            $"Unknown rpcv2Cbor event stream message type '{messageType ?? "<missing>"}'."
        );
    }

    private static Stream RequestStream(SmithyHttpRequest request) =>
        request.Body switch
        {
            SmithyHttpBody.Streaming streaming => streaming.Content,
            SmithyHttpBody.Bytes bytes => new MemoryStream(bytes.Content, writable: false),
            _ => Stream.Null,
        };

    private static Stream ResponseStream(SmithyHttpClientResponse response) =>
        response.Body switch
        {
            SmithyHttpBody.Streaming streaming => streaming.Content,
            SmithyHttpBody.Bytes bytes => new MemoryStream(bytes.Content, writable: false),
            _ => Stream.Null,
        };

    private static void DisposeResponseBody(SmithyHttpClientResponse response)
    {
        if (response.Body is SmithyHttpBody.Streaming streaming)
        {
            streaming.Content.Dispose();
        }
    }

    private static byte[] BodyBytes(SmithyHttpBody body) =>
        body is SmithyHttpBody.Bytes bytes ? bytes.Content : [];

    private static void EnsureEventStreamResponse(SmithyHttpClientResponse response)
    {
        EnsureResponse(response);
        if (response.StatusCode == HttpStatusCode.OK && IsEventStreamContentType(response))
        {
            return;
        }

        DisposeResponseBody(response);
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

    private static Schema? FindEventStreamEventSchema(Schema schema) =>
        schema.Resolved is IStructSchema structure
            ? FindEventStreamEventSchemaCore((dynamic)structure)
            : null;

    private static Schema? FindEventStreamEventSchemaCore<T>(IStructSchema<T> structure)
    {
        var visitor = new EventStreamSchemaVisitor<T>();
        structure.VisitMembers(visitor);
        return visitor.EventSchema;
    }

    private sealed class EventStreamSchemaVisitor<T> : IMemberVisitor<T>
    {
        public Schema? EventSchema { get; private set; }

        public void Visit<TValue>(IMemberSchema<T, TValue> member)
        {
            if (member.Target is not IEventStreamSchema eventStream)
            {
                return;
            }

            if (EventSchema is not null)
            {
                throw new InvalidOperationException("Operation shape has multiple event streams.");
            }

            EventSchema = eventStream.EventSchema;
        }
    }

    private static T DeserializeRequiredBody<T>(ICodec<T> codec, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Response body is required but was empty.");
        }

        return codec.Deserialize(content);
    }

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
