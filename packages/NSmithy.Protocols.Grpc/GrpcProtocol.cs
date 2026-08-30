using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using NSmithy.Codecs.Proto;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;

namespace NSmithy.Protocols.Grpc;

/// <summary>
/// A native gRPC protocol for NSmithy: it speaks the gRPC HTTP/2 wire contract directly, using
/// <see cref="ProtoCodecFactory"/> for message bodies and <see cref="GrpcMessageFraming"/> for the
/// length-prefixed frames — no protoc, Grpc.Tools, or Grpc.Net required. It plugs into the same
/// <see cref="IServiceProtocol"/>/<see cref="IOperationProtocol{TInput, TOutput}"/> abstraction the
/// REST and rpcv2Cbor protocols use, so the generated client/server glue is protocol-agnostic.
/// </summary>
/// <remarks>
/// The <c>grpc-status</c>/<c>grpc-message</c> trailers are protocol-owned content: server halves
/// attach them through <see cref="SmithyHttpServerResponse.Trailers"/>, and the host adapter renders
/// them as HTTP/2 trailers (or leading headers when the connection cannot carry trailers).
/// </remarks>
public sealed class GrpcProtocol : IProtocol
{
    private static readonly ProtoCodecFactory CodecFactory = ProtoCodecFactory.Default;

    internal const string ContentType = "application/grpc+proto";

    /// <summary>Native gRPC requires HTTP/2 without downgrade.</summary>
    public SmithyHttpVersionPreference HttpVersionPreference => SmithyHttpVersionPreference.Http2;
    internal const string GrpcStatusHeader = "grpc-status";
    internal const string GrpcMessageHeader = "grpc-message";

    // gRPC has no native notion of a Smithy error shape id. NSmithy carries it in a custom trailer
    // so the client can dispatch to the modeled error type; this is an NSmithy convention pending a
    // standard Smithy↔gRPC error binding.
    internal const string ErrorShapeHeader = "x-smithy-grpc-error";

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
            if (SmithyRequestModifiers.HasRequestCompression(operation))
            {
                // gRPC compresses per message inside the length-prefixed framing
                // (grpc-encoding), not via Content-Encoding on the HTTP body; applying the
                // HTTP-style transform would produce broken requests. Fail at bind time until
                // message-level compression is implemented.
                throw new NotSupportedException(
                    $"@requestCompression on '{operation.Id}' is not supported by the gRPC "
                        + "protocol yet."
                );
            }

            // Decided once here rather than per shape: an event stream changes how the body is
            // framed, not what the operation's input structure has to satisfy. The events
            // themselves are not covered — the validator skips the event-stream member — since
            // rejecting one mid-stream needs a way to report it after the response has begun.
            var inputValidator = validateInput ? SmithyValidator.FromSchema(operation.Input) : null;

            // Each direction is chosen independently; the four call shapes are their cross product.
            var inputEvent = FindEventStreamEventSchema(operation.Input);
            var outputEvent = FindEventStreamEventSchema(operation.Output);
            RequestStrategy<TInput> request = inputEvent is null
                ? UnaryRequest(operation.Input)
                : StreamingRequest(operation.Input, (dynamic)inputEvent);
            ResponseStrategy<TOutput> response = outputEvent is null
                ? UnaryResponse(operation.Output)
                : StreamingResponse(operation.Output, (dynamic)outputEvent);

            return new OperationProtocol<TInput, TOutput>(
                service,
                operation,
                request,
                response,
                inputValidator
            );
        }
    }

    private readonly record struct RequestStrategy<TInput>(
        Func<TInput, CancellationToken, SmithyHttpBody> Write,
        Func<SmithyHttpRequest, CancellationToken, ValueTask<TInput>> Read
    );

    /// <inheritdoc cref="RequestStrategy{TInput}" />
    private readonly record struct ResponseStrategy<TOutput>(
        Func<TOutput, CancellationToken, SmithyHttpServerResponse> Write,
        Func<SmithyHttpClientResponse, CancellationToken, ValueTask<TOutput>> Read,
        bool IsStreaming
    );

    private static RequestStrategy<TInput> UnaryRequest<TInput>(Schema<TInput> inputSchema)
    {
        if (IsUnit<TInput>(inputSchema))
        {
            return new RequestStrategy<TInput>(
                (_, _) => new SmithyHttpBody.Bytes(GrpcMessageFraming.Frame([])),
                (_, _) => default
            );
        }

        var codec = CodecFactory.FromSchema(inputSchema);
        return new RequestStrategy<TInput>(
            (input, _) =>
                new SmithyHttpBody.Bytes(GrpcMessageFraming.Frame(codec.Serialize(input))),
            (request, _) =>
                ValueTask.FromResult(
                    codec.Deserialize(GrpcMessageFraming.ReadSingle(BodyBytes(request.Body)))
                )
        );
    }

    private static RequestStrategy<TInput> StreamingRequest<TInput, TInputEvent>(
        Schema<TInput> inputSchema,
        Schema<TInputEvent> eventSchema
    )
    {
        var codec = CodecFactory.FromSchema(eventSchema);
        var binding = EventStreamShapeBinding<TInput, TInputEvent>.Create(inputSchema);
        return new RequestStrategy<TInput>(
            (input, cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(input);
                return new SmithyHttpBody.EventStreaming(
                    FrameEventsAsync(binding.GetEvents(input), codec, cancellationToken)
                );
            },
            (request, cancellationToken) =>
                ValueTask.FromResult(
                    binding.Build(
                        ReadRequestEventsAsync(RequestStream(request), codec, cancellationToken)
                    )
                )
        );
    }

    private static ResponseStrategy<TOutput> UnaryResponse<TOutput>(Schema<TOutput> outputSchema)
    {
        if (IsUnit<TOutput>(outputSchema))
        {
            return new ResponseStrategy<TOutput>(
                (_, _) => UnaryGrpcResponse(GrpcMessageFraming.Frame([]), OkTrailers),
                (response, _) =>
                {
                    DisposeResponseBody(response);
                    return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
                },
                IsStreaming: false
            );
        }

        var codec = CodecFactory.FromSchema(outputSchema);
        return new ResponseStrategy<TOutput>(
            (output, _) =>
                UnaryGrpcResponse(GrpcMessageFraming.Frame(codec.Serialize(output)), OkTrailers),
            // An all-default message proto-encodes to a zero-length frame; the codec turns that into
            // an empty instance rather than null.
            (response, cancellationToken) =>
                DeserializeSingleResponseAsync(response, codec, cancellationToken),
            IsStreaming: false
        );
    }

    private static ResponseStrategy<TOutput> StreamingResponse<TOutput, TOutputEvent>(
        Schema<TOutput> outputSchema,
        Schema<TOutputEvent> eventSchema
    )
    {
        var codec = CodecFactory.FromSchema(eventSchema);
        var binding = EventStreamShapeBinding<TOutput, TOutputEvent>.Create(outputSchema);
        return new ResponseStrategy<TOutput>(
            (output, cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(output);
                return StreamingGrpcResponse(
                    FrameEventsAsync(binding.GetEvents(output), codec, cancellationToken),
                    StreamTrailers
                );
            },
            (response, cancellationToken) =>
                ValueTask.FromResult(
                    binding.Build(ReadResponseEventsAsync(response, codec, cancellationToken))
                ),
            IsStreaming: true
        );
    }

    private sealed class OperationProtocol<TInput, TOutput>(
        ServiceSchema service,
        OperationSchema<TInput, TOutput> operation,
        RequestStrategy<TInput> request,
        ResponseStrategy<TOutput> response,
        ISmithyValidator<TInput>? inputValidator
    ) : IOperationProtocol<TInput, TOutput>
    {
        // gRPC full method name: "/{package}.{Service}/{Method}". The proto package mirrors the
        // Smithy namespace, matching what smithy-proto-codegen emits.
        private readonly string methodPath = MethodPath(service, operation.Id.Name);

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
            var message = new SmithyHttpRequest(HttpMethod.Post, methodPath)
            {
                Body = request.Write(input, cancellationToken),
                ContentType = ContentType,
                ExpectStreamingResponse = response.IsStreaming,
            };
            message.Headers["te"] = ["trailers"];
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
            EnsureGrpcStreamingResponse(message);
            return response.Read(message, cancellationToken);
        }

        public bool IsErrorResponse(SmithyHttpClientResponse message) =>
            GrpcProtocol.IsErrorResponse(message);

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse message,
            CancellationToken cancellationToken = default
        ) => DeserializeModeledErrorAsync(HttpErrors, message);

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse message) =>
            serverErrors.TrySerialize(exception, out message);
    }

    private static ValueTask<Exception?> DeserializeModeledErrorAsync(
        IReadOnlyList<HttpOperationError> httpErrors,
        SmithyHttpClientResponse response
    ) =>
        ValueTask.FromResult(
            OperationProtocolErrors.DeserializeModeledError(
                httpErrors,
                response,
                ErrorDiscriminator,
                requiresErrorDiscriminator: true,
                supportsHttpStatusErrorFallback: false
            )
        );

    private static string? ErrorDiscriminator(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return IsErrorResponse(response) ? response.Trailer?.Invoke(ErrorShapeHeader) : null;
    }

    private static (Type, Func<Exception, SmithyHttpServerResponse>) CompileServerError<TError>(
        OperationErrorSchema<TError> error
    )
        where TError : Exception =>
        (
            typeof(TError),
            exception =>
                SerializeGrpcError(
                    error.Schema,
                    (TError)exception,
                    error.Id.ToString(),
                    error.HttpStatusCode
                )
        );

    private static SmithyHttpServerResponse SerializeGrpcError<TError>(
        Schema<TError> errorSchema,
        TError value,
        string errorShapeId,
        int statusCode
    )
    {
        var status = GrpcStatusMapping.FromHttpStatus(statusCode);
        var body = GrpcMessageFraming.Frame(CodecFactory.FromSchema(errorSchema).Serialize(value));
        return UnaryGrpcResponse(
            body,
            _ =>
                [
                    new(GrpcStatusHeader, ((int)status).ToString(CultureInfo.InvariantCulture)),
                    new(GrpcMessageHeader, errorShapeId),
                    new(ErrorShapeHeader, errorShapeId),
                ]
        );
    }

    private static HttpOperationError[] CompileErrors(
        IReadOnlyList<IOperationErrorSchema> errors
    ) => errors.Select(error => (HttpOperationError)CompileError((dynamic)error)).ToArray();

    private static HttpOperationError CompileError<TError>(OperationErrorSchema<TError> error)
        where TError : Exception
    {
        var codec = CodecFactory.FromSchema(error.Schema);
        return new HttpOperationError(
            error.Id,
            error.HttpStatusCode,
            response =>
            {
                var payload = GrpcMessageFraming.ReadSingle(response.Content);
                return codec.Deserialize(payload);
            }
        );
    }

    private abstract class EventStreamShapeBinding<TShape, TEvent>
    {
        public static EventStreamShapeBinding<TShape, TEvent> Create(Schema<TShape> schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            var resolved = schema.Resolved;
            var unwrapped = resolved is INullableSchema nullable
                ? nullable.Target.Resolved
                : resolved;
            return (EventStreamShapeBinding<TShape, TEvent>)unwrapped.Accept(new Visitor());
        }

        public abstract IAsyncEnumerable<TEvent> GetEvents(TShape shape);

        public abstract TShape Build(IAsyncEnumerable<TEvent> events);

        private sealed class Visitor : ISchemaVisitor<object>
        {
            public object VisitBoolean(Schema<bool> schema) => Unsupported();

            public object VisitByte(Schema<sbyte> schema) => Unsupported();

            public object VisitShort(Schema<short> schema) => Unsupported();

            public object VisitInteger(Schema<int> schema) => Unsupported();

            public object VisitLong(Schema<long> schema) => Unsupported();

            public object VisitFloat(Schema<float> schema) => Unsupported();

            public object VisitDouble(Schema<double> schema) => Unsupported();

            public object VisitBigInteger(Schema<System.Numerics.BigInteger> schema) =>
                Unsupported();

            public object VisitBigDecimal(Schema<decimal> schema) => Unsupported();

            public object VisitString(Schema<string> schema) => Unsupported();

            public object VisitBlob(Schema<byte[]> schema) => Unsupported();

            public object VisitTimestamp(Schema<DateTimeOffset> schema) => Unsupported();

            public object VisitDocument(Schema<Document> schema) => Unsupported();

            public object VisitNullable<T>(NullableSchema<T> schema)
                where T : struct => Unsupported();

            public object VisitStreamingBlob(Schema<Stream> schema) => Unsupported();

            public object VisitEventStream<TEventValue>(EventStreamSchema<TEventValue> schema) =>
                Unsupported();

            public object VisitList<TCollection, TElement, TBuilder>(
                IListSchema<TCollection, TElement, TBuilder> schema
            ) => Unsupported();

            public object VisitMap<TDictionary, TValue, TBuilder>(
                IMapSchema<TDictionary, TValue, TBuilder> schema
            ) => Unsupported();

            public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
            {
                if (!typeof(T).IsAssignableTo(typeof(TShape)))
                {
                    return Unsupported();
                }

                var typed = (IStructSchema<TShape, TBuilder>)(object)schema;
                var visitor = new EventStreamMemberVisitor<TShape, TBuilder, TEvent>();
                typed.VisitMembers(visitor);
                if (visitor.MemberCount != 1)
                {
                    throw new NotSupportedException(
                        "gRPC event streaming with non-streaming initial members is not supported."
                    );
                }

                return new TypedEventStreamShapeBinding<TShape, TEvent, TBuilder>(
                    typed,
                    visitor.Member
                        ?? throw new InvalidOperationException(
                            "gRPC event streams require one event stream member."
                        )
                );
            }

            public object VisitUnion<T>(IUnionSchema<T> schema) => Unsupported();

            public object VisitStringEnum<T>(StringEnumSchema<T> schema)
                where T : IStringEnumValue<T> => Unsupported();

            public object VisitIntEnum<T>(IntEnumSchema<T> schema)
                where T : struct, Enum => Unsupported();

            private static object Unsupported() =>
                throw new InvalidOperationException(
                    "gRPC event streams must use a structure shape."
                );
        }
    }

    private sealed class TypedEventStreamShapeBinding<TShape, TEvent, TBuilder>(
        IStructSchema<TShape, TBuilder> structure,
        IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> streamMember
    ) : EventStreamShapeBinding<TShape, TEvent>
    {
        public override IAsyncEnumerable<TEvent> GetEvents(TShape shape) =>
            streamMember.GetValue(shape)
            ?? throw new InvalidOperationException(
                $"Event stream member '{streamMember.Name}' was null."
            );

        public override TShape Build(IAsyncEnumerable<TEvent> events)
        {
            var builder = structure.CreateTypedBuilder();
            streamMember.SetValue(builder, events);
            return structure.Build(builder);
        }
    }

    private sealed class EventStreamMemberVisitor<TShape, TBuilder, TEvent>
        : IMemberVisitor<TShape, TBuilder>
    {
        public IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>>? Member
        {
            get;
            private set;
        }

        public int MemberCount { get; private set; }

        public void Visit<TValue>(IMemberSchema<TShape, TBuilder, TValue> member)
        {
            MemberCount++;
            if (member.Target is not EventStreamSchema<TEvent>)
            {
                return;
            }

            if (Member is not null)
            {
                throw new InvalidOperationException(
                    "gRPC event streams require one event stream member."
                );
            }

            Member = (IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>>)(object)member;
        }
    }

    // ---------------- server response construction ----------------

    private static SmithyHttpServerResponse UnaryGrpcResponse(
        byte[] framedBody,
        Func<Exception?, IReadOnlyList<KeyValuePair<string, string>>> trailers
    )
    {
        var response = new SmithyHttpServerResponse
        {
            StatusCode = (int)HttpStatusCode.OK,
            Body = SingleChunk(framedBody),
            ContentLength = framedBody.Length,
            Trailers = trailers,
        };
        response.Headers["Content-Type"] = [ContentType];
        return response;
    }

    private static SmithyHttpServerResponse StreamingGrpcResponse(
        IAsyncEnumerable<ReadOnlyMemory<byte>> body,
        Func<Exception?, IReadOnlyList<KeyValuePair<string, string>>> trailers
    )
    {
        var response = new SmithyHttpServerResponse
        {
            StatusCode = (int)HttpStatusCode.OK,
            Body = body,
            Trailers = trailers,
        };
        response.Headers["Content-Type"] = [ContentType];
        return response;
    }

    private static IReadOnlyList<KeyValuePair<string, string>> OkTrailers(Exception? _) =>
        [new(GrpcStatusHeader, "0")];

    // A stream that completes cleanly reports OK; a mid-stream failure becomes grpc-status INTERNAL.
    private static IReadOnlyList<KeyValuePair<string, string>> StreamTrailers(Exception? error) =>
        error is null
            ? [new(GrpcStatusHeader, "0")]
            :
            [
                new(
                    GrpcStatusHeader,
                    ((int)GrpcStatus.Internal).ToString(CultureInfo.InvariantCulture)
                ),
                new(GrpcMessageHeader, error.Message),
            ];

    private static Stream RequestStream(SmithyHttpRequest request) =>
        request.Body switch
        {
            SmithyHttpBody.Streaming streaming => streaming.Content,
            SmithyHttpBody.Bytes bytes => new MemoryStream(bytes.Content, writable: false),
            _ => Stream.Null,
        };

    // ---------------- shared wire helpers ----------------

    private static bool IsErrorResponse(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        // A non-zero grpc-status is a modeled/runtime gRPC error.
        var status = response.Trailer?.Invoke(GrpcStatusHeader);
        if (status is not null && !string.Equals(status, "0", StringComparison.Ordinal))
        {
            return true;
        }

        // Anything that is not a 200 application/grpc response is a transport-level failure (a 404,
        // a 500 HTML page, an HTTP/1.1 downgrade, …) — treat it as an error so the client surfaces
        // it instead of trying to parse a non-gRPC body as a frame.
        return response.StatusCode != HttpStatusCode.OK || !IsGrpcContentType(response);
    }

    private static void EnsureGrpcStreamingResponse(SmithyHttpClientResponse response)
    {
        if (response.StatusCode == HttpStatusCode.OK && IsGrpcContentType(response.ContentHeaders))
        {
            return;
        }

        // Transport-level failure (a 404, a 500 page, an HTTP/1.1 downgrade, ...). Release the
        // connection eagerly; the informative error comes from the captured status/headers.
        DisposeResponseBody(response);
        var message =
            response.Headers.TryGetValue(GrpcMessageHeader, out var values) && values.Count > 0
                ? values[0]
                : response.ReasonPhrase;
        throw new InvalidOperationException(
            $"Expected a gRPC stream but received HTTP {(int)response.StatusCode}: {message}"
        );
    }

    /// <summary>
    /// gRPC signals stream success in the grpc-status trailer, which only materializes after the
    /// body is fully read. A non-zero (or missing) status means the server-side call failed.
    /// </summary>
    private static void ThrowIfGrpcError(Func<string, string?>? trailer)
    {
        var status = trailer?.Invoke(GrpcStatusHeader);
        if (status is null)
        {
            throw new InvalidOperationException(
                "gRPC stream ended without a grpc-status trailer; treating it as an incomplete, failed response."
            );
        }

        if (string.Equals(status, "0", StringComparison.Ordinal))
        {
            return;
        }

        var message = trailer!.Invoke(GrpcMessageHeader);
        var name =
            int.TryParse(status, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            && Enum.IsDefined(typeof(GrpcStatus), code)
                ? ((GrpcStatus)code).ToString()
                : "Unknown";
        throw new InvalidOperationException(
            $"gRPC stream failed with status {status} ({name})"
                + (string.IsNullOrEmpty(message) ? "." : $": {message}")
        );
    }

    private static string MethodPath(ServiceSchema service, string operationName) =>
        $"/{service.Id.Namespace}.{service.Id.Name}/{operationName}";

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> SingleChunk(byte[] framed)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return framed;
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> FrameEventsAsync<T>(
        IAsyncEnumerable<T> events,
        ICodec<T> codec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var value in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return GrpcMessageFraming.Frame(codec.Serialize(value));
        }
    }

    private static async IAsyncEnumerable<T> ReadRequestEventsAsync<T>(
        Stream body,
        ICodec<T> codec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var payload in GrpcMessageFraming
                .ReadAllAsync(body, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // An all-default message proto-encodes to zero bytes; deserialize it to an empty
            // instance rather than null. A frame the codec cannot map to a known case (e.g. a newer
            // peer's unknown union case) deserializes to null — skip it for forward-compatibility
            // instead of surfacing a null event.
            var value = codec.Deserialize(payload);
            if (value is not null)
            {
                yield return value;
            }
        }
    }

    private static async IAsyncEnumerable<T> ReadResponseEventsAsync<T>(
        SmithyHttpClientResponse response,
        ICodec<T> codec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var body = ResponseStream(response);
        await using (body.ConfigureAwait(false))
        {
            await foreach (
                var payload in GrpcMessageFraming
                    .ReadAllAsync(body, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                var value = codec.Deserialize(payload);
                if (value is not null)
                {
                    yield return value;
                }
            }
        }

        ThrowIfGrpcError(response.Trailer);
    }

    private static async ValueTask<T> DeserializeSingleResponseAsync<T>(
        SmithyHttpClientResponse response,
        ICodec<T> codec,
        CancellationToken cancellationToken
    )
    {
        var body = ResponseStream(response);
        await using (body.ConfigureAwait(false))
        {
            await foreach (
                var payload in GrpcMessageFraming
                    .ReadAllAsync(body, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                return codec.Deserialize(payload);
            }
        }

        return default!;
    }

    private static bool IsGrpcContentType(SmithyHttpClientResponse response) =>
        IsGrpcContentType(response.ContentHeaders);

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

    private static bool IsGrpcContentType(
        IReadOnlyDictionary<string, IReadOnlyList<string>> contentHeaders
    ) =>
        contentHeaders.TryGetValue("Content-Type", out var contentType)
        && contentType.Any(value =>
            value.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase)
        );

    private static byte[] BodyBytes(SmithyHttpBody body) =>
        body is SmithyHttpBody.Bytes bytes ? bytes.Content : [];

    private static bool IsUnit<T>(Schema schema) =>
        typeof(T) == typeof(SmithyUnit) || Schemas.IsSyntheticUnit(schema);

    private static Schema? FindEventStreamEventSchema<T>(Schema<T> schema)
    {
        if (schema.Resolved is not IStructSchema<T> structure)
        {
            return null;
        }

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
}
