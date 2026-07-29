using System.Net;
using System.Runtime.CompilerServices;
using NSmithy.Codecs.Cbor;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.EventStream;
using NSmithy.Http;

namespace NSmithy.Protocols.RpcV2Cbor;

public sealed class RpcV2CborProtocol : IProtocol
{
    private const string ContentType = "application/cbor";
    private const string EventStreamContentType = "application/vnd.amazon.eventstream";

    // Smithy 2.0 wraps `input: Unit` / `output: Unit` in synthetic structures that carry
    // this trait pointing back to the original `smithy.api#Unit` shape id.
    private static readonly ShapeId SyntheticOriginalShapeId = new(
        "smithy.synthetic",
        "originalShapeId"
    );
    private static readonly string UnitShapeIdString = "smithy.api#Unit";

    /// <summary>Returns true for synthetic unit-derived schemas that carry no members.</summary>
    private static bool IsUnitSchema(Schema schema) =>
        schema.HasTrait(SyntheticOriginalShapeId)
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.Kind == DocumentKind.String
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.AsString() == UnitShapeIdString;

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
        public IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            return new OperationProtocol<TInput, TOutput>(service, operation);
        }

        public IOutputEventStreamOperationProtocol<
            TInput,
            TOutputEvent
        > ForOutputEventStreamOperation<TInput, TOutput, TOutputEvent>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(outputEvent);
            return new OutputEventStreamOperationProtocol<TInput, TOutput, TOutputEvent>(
                service,
                operation,
                outputEvent
            );
        }

        public IInputEventStreamOperationProtocol<
            TInputEvent,
            TOutput
        > ForInputEventStreamOperation<TInput, TInputEvent, TOutput>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(inputEvent);
            return new InputEventStreamOperationProtocol<TInput, TInputEvent, TOutput>(
                service,
                operation,
                inputEvent
            );
        }

        public IDuplexEventStreamOperationProtocol<
            TInputEvent,
            TOutputEvent
        > ForDuplexEventStreamOperation<TInput, TOutput, TInputEvent, TOutputEvent>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            Schema<TOutputEvent> outputEvent
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(inputEvent);
            ArgumentNullException.ThrowIfNull(outputEvent);
            return new DuplexEventStreamOperationProtocol<
                TInput,
                TOutput,
                TInputEvent,
                TOutputEvent
            >(service, operation, inputEvent, outputEvent);
        }
    }

    private sealed class OperationProtocol<TInput, TOutput> : IOperationProtocol<TInput, TOutput>
    {
        private readonly string requestUri;
        private readonly bool inputIsUnit;
        private readonly bool outputIsSmithyUnit;
        private readonly bool outputIsUnit;
        private readonly ICborCodec<TInput> requestCodec;
        private readonly ICborCodec<TOutput> responseCodec;
        private readonly Action<SmithyHttpRequest>? requestTransform;
        private readonly ModeledErrorSerializer serverErrors;

        public OperationProtocol(ServiceSchema service, OperationSchema<TInput, TOutput> operation)
        {
            requestTransform = SmithyRequestModifiers.Compile(operation);
            // The rpcv2Cbor path is service-derived; the protocol owns this wire detail.
            requestUri = $"/service/{service.Id.Name}/operation/{operation.Id.Name}";
            inputIsUnit = typeof(TInput) == typeof(SmithyUnit) || IsUnitSchema(operation.Input);
            outputIsSmithyUnit = typeof(TOutput) == typeof(SmithyUnit);
            outputIsUnit = outputIsSmithyUnit || IsUnitSchema(operation.Output);

            // Built once per operation; the right materialize policy baked in per direction.
            requestCodec = CborCodec.FromSchema(
                operation.Input,
                materializeTopLevelDefaults: false
            );
            responseCodec = CborCodec.FromSchema(
                operation.Output,
                materializeTopLevelDefaults: true
            );
            HttpErrors = CompileErrors(operation.Errors);
            serverErrors = ModeledErrorSerializer.Compile(
                operation.Errors,
                error => CompileServerError((dynamic)error)
            );
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(TInput input)
        {
            var request = BaseRequest(requestUri, ContentType);
            if (!inputIsUnit)
            {
                request.Body = new SmithyHttpBody.Bytes(requestCodec.Serialize(input));
                request.ContentType = ContentType;
            }

            requestTransform?.Invoke(request);
            return request;
        }

        public TOutput DeserializeResponse(SmithyHttpClientResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            if (outputIsSmithyUnit)
            {
                EnsureResponse(response);
                return (TOutput)(object)SmithyUnit.Value;
            }

            return response.Content.Length == 0
                ? default!
                : responseCodec.Deserialize(response.Content);
        }

        public TInput DeserializeRequest(SmithyHttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return default!;
            }

            var content = BodyBytes(request.Body);
            return content.Length == 0 ? default! : requestCodec.Deserialize(content);
        }

        public SmithyHttpServerResponse SerializeResponse(TOutput output)
        {
            if (outputIsUnit)
            {
                return BufferedResponse(
                    200,
                    ReadOnlyMemory<byte>.Empty,
                    headers => headers["Smithy-Protocol"] = ["rpc-v2-cbor"]
                );
            }

            return BufferedResponse(
                200,
                responseCodec.Serialize(output),
                headers =>
                {
                    headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                    headers["Content-Type"] = [ContentType];
                }
            );
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        // rpcv2Cbor errors always carry an explicit __type discriminator; without one the
        // response carries no modeled error, and the HTTP status maps to no error shape.
        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                OperationProtocolErrors.DeserializeModeledError(
                    HttpErrors,
                    response,
                    r => HasResponse(r) ? DeserializeErrorType(r) : null,
                    requiresErrorDiscriminator: true,
                    supportsHttpStatusErrorFallback: false
                )
            );

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);

        private static (Type, Func<Exception, SmithyHttpServerResponse>) CompileServerError<TError>(
            OperationErrorSchema<TError> error
        )
            where TError : Exception =>
            (
                typeof(TError),
                exception =>
                    RpcV2CborProtocol.SerializeError(
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

        private static byte[] BodyBytes(SmithyHttpBody body) =>
            body is SmithyHttpBody.Bytes bytes ? bytes.Content : [];
    }

    private sealed class OutputEventStreamOperationProtocol<TInput, TOutput, TOutputEvent>
        : IOutputEventStreamOperationProtocol<TInput, TOutputEvent>
    {
        private readonly string requestUri;
        private readonly bool inputIsUnit;
        private readonly ICborCodec<TInput>? requestCodec;
        private readonly ICborCodec<TOutputEvent> responseCodec;
        private readonly IUnionSchema responseEvent;

        public OutputEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent
        )
        {
            requestUri = RequestUri(service, operation);
            inputIsUnit = IsUnit<TInput>(operation.Input);
            requestCodec = inputIsUnit
                ? null
                : CborCodec.FromSchema(operation.Input, materializeTopLevelDefaults: false);
            responseCodec = CborCodec.FromSchema(outputEvent);
            responseEvent =
                outputEvent.Resolved as IUnionSchema
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor event streams must target a union schema."
                );
            EnsureNoInitialResponseMembers(operation.Output);
        }

        public SmithyHttpRequest SerializeRequest(TInput input)
        {
            var request = BaseRequest(requestUri, EventStreamContentType);
            if (!inputIsUnit)
            {
                request.Body = new SmithyHttpBody.Bytes(requestCodec!.Serialize(input));
                request.ContentType = ContentType;
            }

            return request;
        }

        public TInput DeserializeRequest(SmithyHttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return default!;
            }

            var content = BodyBytes(request.Body);
            return content.Length == 0 ? default! : requestCodec!.Deserialize(content);
        }

        public IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureEventStreamResponse(response);
            return ReadResponseEventsAsync(response, responseCodec, cancellationToken);
        }

        public SmithyHttpServerResponse SerializeResponse(
            IAsyncEnumerable<TOutputEvent> output,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(output);
            return StreamingResponse(
                FrameEventsAsync(output, responseCodec, responseEvent, cancellationToken)
            );
        }
    }

    private sealed class InputEventStreamOperationProtocol<TInput, TInputEvent, TOutput>
        : IInputEventStreamOperationProtocol<TInputEvent, TOutput>
    {
        private readonly string requestUri;
        private readonly bool outputIsUnit;
        private readonly ICborCodec<TInputEvent> requestCodec;
        private readonly IUnionSchema requestEvent;
        private readonly ICborCodec<TOutput>? responseCodec;

        public InputEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent
        )
        {
            requestUri = RequestUri(service, operation);
            EnsureNoInitialRequestMembers(operation.Input);
            outputIsUnit = IsUnit<TOutput>(operation.Output);
            requestCodec = CborCodec.FromSchema(inputEvent);
            requestEvent =
                inputEvent.Resolved as IUnionSchema
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor event streams must target a union schema."
                );
            responseCodec = outputIsUnit ? null : CborCodec.FromSchema(operation.Output);
        }

        public SmithyHttpRequest SerializeRequest(
            IAsyncEnumerable<TInputEvent> input,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(input);
            return BaseStreamingRequest(
                requestUri,
                ContentType,
                FrameEventsAsync(input, requestCodec, requestEvent, cancellationToken)
            );
        }

        public IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            return ReadRequestEventsAsync(RequestStream(request), requestCodec, cancellationToken);
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureResponse(response);
            if (outputIsUnit)
            {
                DisposeResponseBody(response);
                return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
            }

            return DeserializeSingleResponseAsync(response, responseCodec!, cancellationToken);
        }

        public SmithyHttpServerResponse SerializeResponse(TOutput output)
        {
            if (outputIsUnit)
            {
                return BufferedResponse(
                    200,
                    ReadOnlyMemory<byte>.Empty,
                    headers => headers["Smithy-Protocol"] = ["rpc-v2-cbor"]
                );
            }

            return BufferedResponse(
                200,
                responseCodec!.Serialize(output),
                headers =>
                {
                    headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                    headers["Content-Type"] = [ContentType];
                }
            );
        }
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
    ) : IDuplexEventStreamOperationProtocol<TInputEvent, TOutputEvent>
    {
        private readonly string requestUri = RequestUri(service, operation);
        private readonly ICborCodec<TInputEvent> requestCodec = CborCodec.FromSchema(inputEvent);
        private readonly IUnionSchema requestEvent =
            inputEvent.Resolved as IUnionSchema
            ?? throw new InvalidOperationException(
                "rpcv2Cbor event streams must target a union schema."
            );
        private readonly ICborCodec<TOutputEvent> responseCodec = CborCodec.FromSchema(outputEvent);
        private readonly IUnionSchema responseEvent =
            outputEvent.Resolved as IUnionSchema
            ?? throw new InvalidOperationException(
                "rpcv2Cbor event streams must target a union schema."
            );

        public SmithyHttpRequest SerializeRequest(
            IAsyncEnumerable<TInputEvent> input,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(input);
            EnsureNoInitialRequestMembers(operation.Input);
            EnsureNoInitialResponseMembers(operation.Output);
            return BaseStreamingRequest(
                requestUri,
                EventStreamContentType,
                FrameEventsAsync(input, requestCodec, requestEvent, cancellationToken)
            );
        }

        public IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            return ReadRequestEventsAsync(RequestStream(request), requestCodec, cancellationToken);
        }

        public IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureEventStreamResponse(response);
            return ReadResponseEventsAsync(response, responseCodec, cancellationToken);
        }

        public SmithyHttpServerResponse SerializeResponse(
            IAsyncEnumerable<TOutputEvent> output,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(output);
            return StreamingResponse(
                FrameEventsAsync(output, responseCodec, responseEvent, cancellationToken)
            );
        }
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
            CborCodec.SerializeError(errorSchema, error, errorShapeId),
            headers =>
            {
                headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                headers["Content-Type"] = [ContentType];
            }
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

    private static SmithyHttpRequest BaseStreamingRequest(
        string requestUri,
        string accept,
        IAsyncEnumerable<ReadOnlyMemory<byte>> body
    )
    {
        var request = BaseRequest(requestUri, accept);
        request.Body = new SmithyHttpBody.EventStreaming(body);
        request.ContentType = EventStreamContentType;
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
            var message = new EventStreamMessage(
                new Dictionary<string, EventStreamHeaderValue>
                {
                    [EventStreamHeaders.MessageType] = new EventStreamHeaderValue.Text(
                        EventStreamHeaders.EventMessageType
                    ),
                    [EventStreamHeaders.EventType] = new EventStreamHeaderValue.Text(
                        eventSchema.GetCaseObject(value!).Name
                    ),
                    [EventStreamHeaders.ContentType] = new EventStreamHeaderValue.Text(ContentType),
                },
                codec.Serialize(value)
            );
            yield return message.Encode();
        }
    }

    private static async IAsyncEnumerable<T> ReadRequestEventsAsync<T>(
        Stream body,
        ICborCodec<T> codec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var message in EventStreamMessageReader.ReadAllAsync(body, cancellationToken)
        )
        {
            var value = DeserializeEventMessage(codec, message);
            if (value is not null)
            {
                yield return value;
            }
        }
    }

    private static async IAsyncEnumerable<T> ReadResponseEventsAsync<T>(
        SmithyHttpClientResponse response,
        ICborCodec<T> codec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var body = ResponseStream(response);
        await using (body.ConfigureAwait(false))
        {
            await foreach (
                var message in EventStreamMessageReader.ReadAllAsync(body, cancellationToken)
            )
            {
                var value = DeserializeEventMessage(codec, message);
                if (value is not null)
                {
                    yield return value;
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

        return codec.Deserialize(message.Payload.ToArray());
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
        typeof(T) == typeof(SmithyUnit) || IsUnitSchema(schema);

    private static void EnsureNoInitialRequestMembers<T>(Schema<T> input) =>
        EnsureNoInitialMembers(input, "request");

    private static void EnsureNoInitialResponseMembers<T>(Schema<T> output) =>
        EnsureNoInitialMembers(output, "response");

    private static void EnsureNoInitialMembers(Schema schema, string direction)
    {
        if (IsUnitSchema(schema))
        {
            return;
        }

        if (schema.Resolved is IStructSchema structure && structure.Members.Count <= 1)
        {
            return;
        }

        throw new NotSupportedException(
            $"rpcv2Cbor event streaming with non-streaming initial {direction} members is not supported by the current generated streaming surface."
        );
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
