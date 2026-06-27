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
/// This is the unary shape. Streaming RPCs (the <c>stream</c> keyword in the proto) would be a
/// separate streaming protocol interface. The <c>grpc-status</c>/<c>grpc-message</c> trailers are
/// modeled on the <see cref="SmithyHttpResponse.Headers"/> dictionary; the transport renders them as
/// HTTP/2 trailers.
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
            return new OperationProtocol<TInput, TOutput>(service, operation);
        }

        public IServerEventStreamOperationProtocol<
            TInput,
            TOutputEvent
        > ForServerEventStreamOperation<TInput, TOutput, TOutputEvent>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(outputEvent);
            return new ServerEventStreamOperationProtocol<TInput, TOutput, TOutputEvent>(
                service,
                operation,
                outputEvent
            );
        }

        public IClientEventStreamOperationProtocol<
            TInputEvent,
            TOutput
        > ForClientEventStreamOperation<TInput, TInputEvent, TOutput>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(inputEvent);
            return new ClientEventStreamOperationProtocol<TInput, TInputEvent, TOutput>(
                service,
                operation,
                inputEvent
            );
        }

        public IBidirectionalEventStreamOperationProtocol<
            TInputEvent,
            TOutputEvent
        > ForBidirectionalEventStreamOperation<TInput, TOutput, TInputEvent, TOutputEvent>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            Schema<TOutputEvent> outputEvent
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(inputEvent);
            ArgumentNullException.ThrowIfNull(outputEvent);
            return new BidirectionalEventStreamOperationProtocol<
                TInput,
                TOutput,
                TInputEvent,
                TOutputEvent
            >(service, operation, inputEvent, outputEvent);
        }
    }

    private sealed class OperationProtocol<TInput, TOutput> : IOperationProtocol<TInput, TOutput>
    {
        private readonly string methodPath;
        private readonly bool inputIsUnit;
        private readonly bool outputIsUnit;
        private readonly IProtoCodec<TInput>? requestCodec;
        private readonly IProtoCodec<TOutput>? responseCodec;

        public OperationProtocol(ServiceSchema service, OperationSchema<TInput, TOutput> operation)
        {
            // gRPC full method name: "/{package}.{Service}/{Method}". The proto package mirrors the
            // Smithy namespace, matching what smithy-proto-codegen emits.
            methodPath = $"/{service.Id.Namespace}.{service.Id.Name}/{operation.Id.Name}";

            inputIsUnit = IsUnit<TInput>(operation.Input);
            outputIsUnit = IsUnit<TOutput>(operation.Output);
            requestCodec = inputIsUnit ? null : ProtoCodec.FromSchema(operation.Input);
            responseCodec = outputIsUnit ? null : ProtoCodec.FromSchema(operation.Output);
        }

        public SmithyHttpRequest SerializeRequest(TInput input)
        {
            var request = new SmithyHttpRequest(HttpMethod.Post, methodPath)
            {
                Content = GrpcMessageFraming.Frame(
                    inputIsUnit ? [] : requestCodec!.Serialize(input)
                ),
                ContentType = ContentType,
            };
            request.Headers["te"] = ["trailers"];
            return request;
        }

        public TOutput DeserializeResponse(SmithyHttpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureGrpcResponse(response);
            if (outputIsUnit)
            {
                return (TOutput)(object)SmithyUnit.Value;
            }

            // An all-default message proto-encodes to zero bytes; deserialize it to an empty instance
            // rather than null.
            var payload = GrpcMessageFraming.ReadSingle(response.Content);
            return responseCodec!.Deserialize(payload);
        }

        public TInput DeserializeRequest(SmithyHttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return default!;
            }

            var payload = GrpcMessageFraming.ReadSingle(request.Content ?? []);
            return requestCodec!.Deserialize(payload);
        }

        public SmithyHttpResponse SerializeResponse(TOutput output)
        {
            var body = GrpcMessageFraming.Frame(
                outputIsUnit ? [] : responseCodec!.Serialize(output)
            );
            return new SmithyHttpResponse(
                HttpStatusCode.OK,
                null,
                body,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [GrpcStatusHeader] =
                    [
                        ((int)GrpcStatus.Ok).ToString(CultureInfo.InvariantCulture),
                    ],
                },
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = [ContentType],
                }
            );
        }

        public bool IsErrorResponse(SmithyHttpResponse response) =>
            GrpcProtocol.IsErrorResponse(response);

        public string? GetErrorDiscriminator(SmithyHttpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            return
                IsErrorResponse(response)
                && response.Headers.TryGetValue(ErrorShapeHeader, out var values)
                && values.Count > 0
                ? values[0]
                : null;
        }

        public bool RequiresErrorDiscriminator => true;

        public bool SupportsHttpStatusErrorFallback => false;

        public TError DeserializeError<TError>(
            Schema<TError> errorSchema,
            SmithyHttpResponse response
        )
        {
            ArgumentNullException.ThrowIfNull(errorSchema);
            ArgumentNullException.ThrowIfNull(response);
            var payload = GrpcMessageFraming.ReadSingle(response.Content);
            return ProtoCodec.FromSchema(errorSchema).Deserialize(payload);
        }

        public SmithyHttpResponse SerializeError<TError>(
            Schema<TError> errorSchema,
            TError value,
            string errorShapeId,
            int statusCode
        )
        {
            ArgumentNullException.ThrowIfNull(errorSchema);
            ArgumentNullException.ThrowIfNull(errorShapeId);

            var status = GrpcStatusMapping.FromHttpStatus(statusCode);
            var body = GrpcMessageFraming.Frame(
                ProtoCodec.FromSchema(errorSchema).Serialize(value)
            );
            return new SmithyHttpResponse(
                HttpStatusCode.OK,
                null,
                body,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [GrpcStatusHeader] = [((int)status).ToString(CultureInfo.InvariantCulture)],
                    [GrpcMessageHeader] = [errorShapeId],
                    [ErrorShapeHeader] = [errorShapeId],
                },
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = [ContentType],
                }
            );
        }
    }

    private sealed class ServerEventStreamOperationProtocol<TInput, TOutput, TOutputEvent>
        : IServerEventStreamOperationProtocol<TInput, TOutputEvent>
    {
        private readonly string methodPath;
        private readonly bool inputIsUnit;
        private readonly IProtoCodec<TInput>? requestCodec;
        private readonly IProtoCodec<TOutputEvent> responseCodec;

        public ServerEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent
        )
        {
            methodPath = MethodPath(service, operation.Id.Name);
            inputIsUnit = IsUnit<TInput>(operation.Input);
            requestCodec = inputIsUnit ? null : ProtoCodec.FromSchema(operation.Input);
            responseCodec = ProtoCodec.FromSchema(outputEvent);
        }

        public SmithyEventStreamHttpRequest SerializeRequest(TInput input)
        {
            var request = new SmithyEventStreamHttpRequest(HttpMethod.Post, methodPath)
            {
                Events = SingleEvent(inputIsUnit ? [] : requestCodec!.Serialize(input)),
                ContentType = ContentType,
            };
            request.Headers["te"] = ["trailers"];
            return request;
        }

        public TInput DeserializeRequest(SmithyHttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return default!;
            }

            var payload = GrpcMessageFraming.ReadSingle(request.Content ?? []);
            return requestCodec!.Deserialize(payload);
        }

        public IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
            SmithyEventStreamHttpResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureGrpcResponse(response);
            return DeserializeEventsAsync(response.Events, responseCodec, cancellationToken);
        }

        public IAsyncEnumerable<SmithyEventFrame> SerializeResponseEventsAsync(
            IAsyncEnumerable<TOutputEvent> output,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(output);
            return SerializeEventsAsync(output, responseCodec, cancellationToken);
        }
    }

    private sealed class ClientEventStreamOperationProtocol<TInput, TInputEvent, TOutput>
        : IClientEventStreamOperationProtocol<TInputEvent, TOutput>
    {
        private readonly string methodPath;
        private readonly bool outputIsUnit;
        private readonly IProtoCodec<TInputEvent> requestCodec;
        private readonly IProtoCodec<TOutput>? responseCodec;

        public ClientEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent
        )
        {
            methodPath = MethodPath(service, operation.Id.Name);
            outputIsUnit = IsUnit<TOutput>(operation.Output);
            requestCodec = ProtoCodec.FromSchema(inputEvent);
            responseCodec = outputIsUnit ? null : ProtoCodec.FromSchema(operation.Output);
        }

        public SmithyEventStreamHttpRequest SerializeRequest(
            IAsyncEnumerable<TInputEvent> input,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(input);
            var request = new SmithyEventStreamHttpRequest(HttpMethod.Post, methodPath)
            {
                Events = SerializeEventsAsync(input, requestCodec, cancellationToken),
                ContentType = ContentType,
            };
            request.Headers["te"] = ["trailers"];
            return request;
        }

        public IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
            SmithyEventStreamHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            return DeserializeEventsAsync(request.Events, requestCodec, cancellationToken);
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyEventStreamHttpResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureGrpcResponse(response);
            if (outputIsUnit)
            {
                return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
            }

            return DeserializeSingleResponseAsync(
                response.Events,
                responseCodec!,
                cancellationToken
            );
        }

        public SmithyEventFrame SerializeResponse(TOutput output)
        {
            return new SmithyEventFrame(outputIsUnit ? [] : responseCodec!.Serialize(output));
        }
    }

    private sealed class BidirectionalEventStreamOperationProtocol<
        TInput,
        TOutput,
        TInputEvent,
        TOutputEvent
    > : IBidirectionalEventStreamOperationProtocol<TInputEvent, TOutputEvent>
    {
        private readonly string methodPath;
        private readonly IProtoCodec<TInputEvent> requestCodec;
        private readonly IProtoCodec<TOutputEvent> responseCodec;

        public BidirectionalEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            Schema<TOutputEvent> outputEvent
        )
        {
            methodPath = MethodPath(service, operation.Id.Name);
            requestCodec = ProtoCodec.FromSchema(inputEvent);
            responseCodec = ProtoCodec.FromSchema(outputEvent);
        }

        public SmithyEventStreamHttpRequest SerializeRequest(
            IAsyncEnumerable<TInputEvent> input,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(input);
            var request = new SmithyEventStreamHttpRequest(HttpMethod.Post, methodPath)
            {
                Events = SerializeEventsAsync(input, requestCodec, cancellationToken),
                ContentType = ContentType,
            };
            request.Headers["te"] = ["trailers"];
            return request;
        }

        public IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
            SmithyEventStreamHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            return DeserializeEventsAsync(request.Events, requestCodec, cancellationToken);
        }

        public IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
            SmithyEventStreamHttpResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureGrpcResponse(response);
            return DeserializeEventsAsync(response.Events, responseCodec, cancellationToken);
        }

        public IAsyncEnumerable<SmithyEventFrame> SerializeResponseEventsAsync(
            IAsyncEnumerable<TOutputEvent> output,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(output);
            return SerializeEventsAsync(output, responseCodec, cancellationToken);
        }
    }

    private static bool IsErrorResponse(SmithyHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        // A non-zero grpc-status is a modeled/runtime gRPC error.
        if (
            response.Headers.TryGetValue(GrpcStatusHeader, out var values)
            && values.Count > 0
            && !string.Equals(values[0], "0", StringComparison.Ordinal)
        )
        {
            return true;
        }

        // Anything that is not a 200 application/grpc response is a transport-level failure (a 404,
        // a 500 HTML page, an HTTP/1.1 downgrade, …) — treat it as an error so the client surfaces
        // it instead of trying to parse a non-gRPC body as a frame.
        return response.StatusCode != System.Net.HttpStatusCode.OK || !IsGrpcContentType(response);
    }

    private static void EnsureGrpcResponse(SmithyEventStreamHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode == HttpStatusCode.OK && IsGrpcContentType(response))
        {
            return;
        }

        var message =
            response.Headers.TryGetValue(GrpcMessageHeader, out var values) && values.Count > 0
                ? values[0]
                : response.ReasonPhrase;
        throw new InvalidOperationException(
            $"Expected a gRPC stream but received HTTP {(int)response.StatusCode}: {message}"
        );
    }

    private static string MethodPath(ServiceSchema service, string operationName) =>
        $"/{service.Id.Namespace}.{service.Id.Name}/{operationName}";

    private static async IAsyncEnumerable<SmithyEventFrame> SingleEvent(byte[] payload)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new SmithyEventFrame(payload);
    }

    private static async IAsyncEnumerable<SmithyEventFrame> SerializeEventsAsync<T>(
        IAsyncEnumerable<T> events,
        IProtoCodec<T> codec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var value in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return new SmithyEventFrame(codec.Serialize(value));
        }
    }

    private static async IAsyncEnumerable<T> DeserializeEventsAsync<T>(
        IAsyncEnumerable<SmithyEventFrame> events,
        IProtoCodec<T> codec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var frame in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            // An all-default message proto-encodes to zero bytes; deserialize it to an empty
            // instance rather than null. A frame the codec cannot map to a known case (e.g. a newer
            // peer's unknown union case) deserializes to null — skip it for forward-compatibility
            // instead of surfacing a null event.
            var value = codec.Deserialize(frame.Payload);
            if (value is not null)
            {
                yield return value;
            }
        }
    }

    private static async ValueTask<T> DeserializeSingleResponseAsync<T>(
        IAsyncEnumerable<SmithyEventFrame> events,
        IProtoCodec<T> codec,
        CancellationToken cancellationToken
    )
    {
        await foreach (
            var frame in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            return codec.Deserialize(frame.Payload);
        }

        return default!;
    }

    private static bool IsGrpcContentType(SmithyHttpResponse response) =>
        IsGrpcContentType(response.ContentHeaders);

    private static bool IsGrpcContentType(SmithyEventStreamHttpResponse response) =>
        IsGrpcContentType(response.ContentHeaders);

    private static bool IsGrpcContentType(
        IReadOnlyDictionary<string, IReadOnlyList<string>> contentHeaders
    ) =>
        contentHeaders.TryGetValue("Content-Type", out var contentType)
        && contentType.Any(value =>
            value.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase)
        );

    private static void EnsureGrpcResponse(SmithyHttpResponse response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.OK && IsGrpcContentType(response))
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
}
