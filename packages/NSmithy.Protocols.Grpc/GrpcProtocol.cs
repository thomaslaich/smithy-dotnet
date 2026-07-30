using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using NSmithy.Codecs.Proto;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.Grpc;

/// <summary>
/// A native gRPC protocol for NSmithy: it speaks the gRPC HTTP/2 wire contract directly, using
/// <see cref="ProtoCodec"/> for message bodies and <see cref="GrpcMessageFraming"/> for the
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
    internal const string ContentType = "application/grpc+proto";

    /// <summary>Native gRPC runs over HTTP/2.</summary>
    public bool RequiresHttp2 => true;
    internal const string GrpcStatusHeader = "grpc-status";
    internal const string GrpcMessageHeader = "grpc-message";

    // gRPC has no native notion of a Smithy error shape id. NSmithy carries it in a custom trailer
    // so the client can dispatch to the modeled error type; this is an NSmithy convention pending a
    // standard Smithy↔gRPC error binding.
    internal const string ErrorShapeHeader = "x-smithy-grpc-error";

    private static readonly ShapeId SyntheticOriginalShapeId = new(
        "smithy.synthetic",
        "originalShapeId"
    );

    public IServiceProtocol ForService(ServiceSchema service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new ServiceProtocol(service);
    }

    private sealed class ServiceProtocol(ServiceSchema service) : IServiceProtocol
    {
        public IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
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

            var inputEvent = FindEventStreamEventSchema(operation.Input);
            var outputEvent = FindEventStreamEventSchema(operation.Output);
            return (inputEvent, outputEvent) switch
            {
                (null, null) => new OperationProtocol<TInput, TOutput>(service, operation),
                (null, not null) => CreateOutputEventStreamProtocol(
                    operation,
                    (dynamic)outputEvent
                ),
                (not null, null) => CreateInputEventStreamProtocol(operation, (dynamic)inputEvent),
                (not null, not null) => CreateDuplexEventStreamProtocol(
                    operation,
                    (dynamic)inputEvent,
                    (dynamic)outputEvent
                ),
            };
        }

        private OutputEventStreamOperationProtocol<
            TInput,
            TOutput,
            TOutputEvent
        > CreateOutputEventStreamProtocol<TInput, TOutput, TOutputEvent>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent
        ) =>
            new OutputEventStreamOperationProtocol<TInput, TOutput, TOutputEvent>(
                service,
                operation,
                outputEvent
            );

        private InputEventStreamOperationProtocol<
            TInput,
            TInputEvent,
            TOutput
        > CreateInputEventStreamProtocol<TInput, TInputEvent, TOutput>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent
        ) =>
            new InputEventStreamOperationProtocol<TInput, TInputEvent, TOutput>(
                service,
                operation,
                inputEvent
            );

        private DuplexEventStreamOperationProtocol<
            TInput,
            TOutput,
            TInputEvent,
            TOutputEvent
        > CreateDuplexEventStreamProtocol<TInput, TOutput, TInputEvent, TOutputEvent>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            Schema<TOutputEvent> outputEvent
        ) =>
            new DuplexEventStreamOperationProtocol<TInput, TOutput, TInputEvent, TOutputEvent>(
                service,
                operation,
                inputEvent,
                outputEvent
            );
    }

    private sealed class OperationProtocol<TInput, TOutput> : IOperationProtocol<TInput, TOutput>
    {
        private readonly string methodPath;
        private readonly bool inputIsUnit;
        private readonly bool outputIsUnit;
        private readonly IProtoCodec<TInput>? requestCodec;
        private readonly IProtoCodec<TOutput>? responseCodec;
        private readonly ModeledErrorSerializer serverErrors;

        public OperationProtocol(ServiceSchema service, OperationSchema<TInput, TOutput> operation)
        {
            // gRPC full method name: "/{package}.{Service}/{Method}". The proto package mirrors the
            // Smithy namespace, matching what smithy-proto-codegen emits.
            methodPath = MethodPath(service, operation.Id.Name);

            inputIsUnit = IsUnit<TInput>(operation.Input);
            outputIsUnit = IsUnit<TOutput>(operation.Output);
            requestCodec = inputIsUnit ? null : ProtoCodec.FromSchema(operation.Input);
            responseCodec = outputIsUnit ? null : ProtoCodec.FromSchema(operation.Output);
            HttpErrors = CompileErrors(operation.Errors);
            serverErrors = ModeledErrorSerializer.Compile(
                operation.Errors,
                error => CompileServerError((dynamic)error)
            );
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            var request = new SmithyHttpRequest(HttpMethod.Post, methodPath)
            {
                Body = new SmithyHttpBody.Bytes(
                    GrpcMessageFraming.Frame(inputIsUnit ? [] : requestCodec!.Serialize(input))
                ),
                ContentType = ContentType,
            };
            request.Headers["te"] = ["trailers"];
            return request;
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureGrpcStreamingResponse(response);
            if (outputIsUnit)
            {
                return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
            }

            // An all-default message proto-encodes to zero bytes; deserialize it to an empty instance
            // rather than null.
            var payload = GrpcMessageFraming.ReadSingle(response.Content);
            return ValueTask.FromResult(responseCodec!.Deserialize(payload));
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return ValueTask.FromResult<TInput>(default!);
            }

            var payload = GrpcMessageFraming.ReadSingle(BodyBytes(request.Body));
            return ValueTask.FromResult(requestCodec!.Deserialize(payload));
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        ) =>
            UnaryGrpcResponse(
                GrpcMessageFraming.Frame(outputIsUnit ? [] : responseCodec!.Serialize(output)),
                OkTrailers
            );

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            GrpcProtocol.IsErrorResponse(response);

        // gRPC errors are discriminated by the error-shape trailer; the HTTP status is always
        // 200 and maps to no error shape.
        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                OperationProtocolErrors.DeserializeModeledError(
                    HttpErrors,
                    response,
                    ErrorDiscriminator,
                    requiresErrorDiscriminator: true,
                    supportsHttpStatusErrorFallback: false
                )
            );

        private static string? ErrorDiscriminator(SmithyHttpClientResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            return GrpcProtocol.IsErrorResponse(response)
                ? response.Trailer?.Invoke(ErrorShapeHeader)
                : null;
        }

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);

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
            var body = GrpcMessageFraming.Frame(
                ProtoCodec.FromSchema(errorSchema).Serialize(value)
            );
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
            var codec = ProtoCodec.FromSchema(error.Schema);
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
        var body = GrpcMessageFraming.Frame(ProtoCodec.FromSchema(errorSchema).Serialize(value));
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
        var codec = ProtoCodec.FromSchema(error.Schema);
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

    private sealed class OutputEventStreamOperationProtocol<TInput, TOutput, TOutputEvent>
        : IOperationProtocol<TInput, TOutput>
    {
        private readonly string methodPath;
        private readonly bool inputIsUnit;
        private readonly IProtoCodec<TInput>? requestCodec;
        private readonly IProtoCodec<TOutputEvent> responseCodec;
        private readonly EventStreamShapeBinding<TOutput, TOutputEvent> outputBinding;
        private readonly ModeledErrorSerializer serverErrors;

        public OutputEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent
        )
        {
            methodPath = MethodPath(service, operation.Id.Name);
            inputIsUnit = IsUnit<TInput>(operation.Input);
            requestCodec = inputIsUnit ? null : ProtoCodec.FromSchema(operation.Input);
            responseCodec = ProtoCodec.FromSchema(outputEvent);
            outputBinding = EventStreamShapeBinding<TOutput, TOutputEvent>.Create(operation.Output);
            HttpErrors = CompileErrors(operation.Errors);
            serverErrors = ModeledErrorSerializer.Compile(
                operation.Errors,
                error => CompileServerError((dynamic)error)
            );
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        // An output stream's request is unary — one framed message, so an ordinary Bytes body.
        // The response, however, is a live event stream, so the runtime must read it in Stream mode.
        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            var request = new SmithyHttpRequest(HttpMethod.Post, methodPath)
            {
                Body = new SmithyHttpBody.Bytes(
                    GrpcMessageFraming.Frame(inputIsUnit ? [] : requestCodec!.Serialize(input))
                ),
                ContentType = ContentType,
                ExpectStreamingResponse = true,
            };
            request.Headers["te"] = ["trailers"];
            return request;
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return ValueTask.FromResult<TInput>(default!);
            }

            var payload = GrpcMessageFraming.ReadSingle(BodyBytes(request.Body));
            return ValueTask.FromResult(requestCodec!.Deserialize(payload));
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureGrpcStreamingResponse(response);
            return ValueTask.FromResult(
                outputBinding.Build(
                    ReadResponseEventsAsync(response, responseCodec, cancellationToken)
                )
            );
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(output);
            return StreamingGrpcResponse(
                FrameEventsAsync(outputBinding.GetEvents(output), responseCodec, cancellationToken),
                StreamTrailers
            );
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            GrpcProtocol.IsErrorResponse(response);

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => DeserializeModeledErrorAsync(HttpErrors, response);

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);
    }

    private sealed class InputEventStreamOperationProtocol<TInput, TInputEvent, TOutput>
        : IOperationProtocol<TInput, TOutput>
    {
        private readonly string methodPath;
        private readonly bool outputIsUnit;
        private readonly IProtoCodec<TInputEvent> requestCodec;
        private readonly IProtoCodec<TOutput>? responseCodec;
        private readonly EventStreamShapeBinding<TInput, TInputEvent> inputBinding;
        private readonly ModeledErrorSerializer serverErrors;

        public InputEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent
        )
        {
            methodPath = MethodPath(service, operation.Id.Name);
            outputIsUnit = IsUnit<TOutput>(operation.Output);
            requestCodec = ProtoCodec.FromSchema(inputEvent);
            responseCodec = outputIsUnit ? null : ProtoCodec.FromSchema(operation.Output);
            inputBinding = EventStreamShapeBinding<TInput, TInputEvent>.Create(operation.Input);
            HttpErrors = CompileErrors(operation.Errors);
            serverErrors = ModeledErrorSerializer.Compile(
                operation.Errors,
                error => CompileServerError((dynamic)error)
            );
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(input);
            var request = new SmithyHttpRequest(HttpMethod.Post, methodPath)
            {
                Body = new SmithyHttpBody.EventStreaming(
                    FrameEventsAsync(inputBinding.GetEvents(input), requestCodec, cancellationToken)
                ),
                ContentType = ContentType,
            };
            request.Headers["te"] = ["trailers"];
            return request;
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            return ValueTask.FromResult(
                inputBinding.Build(
                    ReadRequestEventsAsync(RequestStream(request), requestCodec, cancellationToken)
                )
            );
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureGrpcStreamingResponse(response);
            if (outputIsUnit)
            {
                DisposeResponseBody(response);
                return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
            }

            return DeserializeSingleResponseAsync(response, responseCodec!, cancellationToken);
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        ) =>
            UnaryGrpcResponse(
                GrpcMessageFraming.Frame(outputIsUnit ? [] : responseCodec!.Serialize(output)),
                OkTrailers
            );

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            GrpcProtocol.IsErrorResponse(response);

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => DeserializeModeledErrorAsync(HttpErrors, response);

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);
    }

    private sealed class DuplexEventStreamOperationProtocol<
        TInput,
        TOutput,
        TInputEvent,
        TOutputEvent
    >(
        ServiceSchema service,
        OperationSchema<TInput, TOutput> operation,
        Schema<TInputEvent> inputEvent,
        Schema<TOutputEvent> outputEvent
    ) : IOperationProtocol<TInput, TOutput>
    {
        private readonly string methodPath = MethodPath(service, operation.Id.Name);
        private readonly IProtoCodec<TInputEvent> requestCodec = ProtoCodec.FromSchema(inputEvent);
        private readonly IProtoCodec<TOutputEvent> responseCodec = ProtoCodec.FromSchema(
            outputEvent
        );
        private readonly ModeledErrorSerializer serverErrors = ModeledErrorSerializer.Compile(
            operation.Errors,
            error => CompileServerError((dynamic)error)
        );
        private readonly EventStreamShapeBinding<TInput, TInputEvent> inputBinding =
            EventStreamShapeBinding<TInput, TInputEvent>.Create(operation.Input);
        private readonly EventStreamShapeBinding<TOutput, TOutputEvent> outputBinding =
            EventStreamShapeBinding<TOutput, TOutputEvent>.Create(operation.Output);

        public IReadOnlyList<HttpOperationError> HttpErrors { get; } =
            CompileErrors(operation.Errors);

        // Duplex streams both directions, so the response must be read in Stream mode.
        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(input);
            var request = new SmithyHttpRequest(HttpMethod.Post, methodPath)
            {
                Body = new SmithyHttpBody.EventStreaming(
                    FrameEventsAsync(inputBinding.GetEvents(input), requestCodec, cancellationToken)
                ),
                ContentType = ContentType,
                ExpectStreamingResponse = true,
            };
            request.Headers["te"] = ["trailers"];
            return request;
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            return ValueTask.FromResult(
                inputBinding.Build(
                    ReadRequestEventsAsync(RequestStream(request), requestCodec, cancellationToken)
                )
            );
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureGrpcResponse(response);
            return ValueTask.FromResult(
                outputBinding.Build(
                    ReadResponseEventsAsync(response, responseCodec, cancellationToken)
                )
            );
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(output);
            return StreamingGrpcResponse(
                FrameEventsAsync(outputBinding.GetEvents(output), responseCodec, cancellationToken),
                StreamTrailers
            );
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            GrpcProtocol.IsErrorResponse(response);

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => DeserializeModeledErrorAsync(HttpErrors, response);

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);
    }

    private sealed class EventStreamShapeBinding<TShape, TEvent>
    {
        private EventStreamShapeBinding(
            IStructSchema<TShape> structure,
            IMemberSchema<TShape> streamMember
        )
        {
            Structure = structure;
            StreamMember = streamMember;
        }

        private IStructSchema<TShape> Structure { get; }

        private IMemberSchema<TShape> StreamMember { get; }

        public static EventStreamShapeBinding<TShape, TEvent> Create(Schema<TShape> schema)
        {
            if (schema.Resolved is not IStructSchema<TShape> structure)
            {
                throw new InvalidOperationException(
                    "gRPC event streams must use a structure shape."
                );
            }

            var streamMember =
                structure.TypedMembers.SingleOrDefault(member =>
                    member.Target is IEventStreamSchema
                )
                ?? throw new InvalidOperationException(
                    "gRPC event streams require one event stream member."
                );
            if (structure.TypedMembers.Count != 1)
            {
                throw new NotSupportedException(
                    "gRPC event streaming with non-streaming initial members is not supported."
                );
            }

            return new EventStreamShapeBinding<TShape, TEvent>(structure, streamMember);
        }

        public IAsyncEnumerable<TEvent> GetEvents(TShape shape)
        {
            var value = StreamMember.GetObject(shape!);
            return value as IAsyncEnumerable<TEvent>
                ?? throw new InvalidOperationException(
                    $"Event stream member '{StreamMember.Name}' was null."
                );
        }

        public TShape Build(IAsyncEnumerable<TEvent> events)
        {
            var builder = Structure.CreateBuilder();
            StreamMember.SetObject(builder, events);
            return (TShape)Structure.BuildObject(builder);
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
        IProtoCodec<T> codec,
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
        IProtoCodec<T> codec,
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
        IProtoCodec<T> codec,
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
        IProtoCodec<T> codec,
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

    private static void EnsureGrpcResponse(SmithyHttpClientResponse response)
    {
        if (response.StatusCode == HttpStatusCode.OK && IsGrpcContentType(response))
        {
            return;
        }

        var message =
            response.Headers.TryGetValue(GrpcMessageHeader, out var values) && values.Count > 0
                ? values[0]
                : response.ContentText;
        throw new InvalidOperationException(
            $"Expected a gRPC response but received HTTP {(int)response.StatusCode}: {message}"
        );
    }

    private static bool IsUnit<T>(Schema schema) =>
        typeof(T) == typeof(SmithyUnit)
        || (
            schema.GetTrait(SyntheticOriginalShapeId)?.Value.Kind == DocumentKind.String
            && schema.GetTrait(SyntheticOriginalShapeId)?.Value.AsString() == "smithy.api#Unit"
        );

    private static Schema? FindEventStreamEventSchema(Schema schema) =>
        schema.Resolved is IStructSchema structure
            ? structure
                .Members.Select(member => member.Target)
                .OfType<IEventStreamSchema>()
                .Select(eventStream => eventStream.EventSchema)
                .SingleOrDefault()
            : null;
}
