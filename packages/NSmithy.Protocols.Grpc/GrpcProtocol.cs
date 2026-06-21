using System.Globalization;
using System.Net;
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

            var payload = GrpcMessageFraming.ReadSingle(response.Content);
            return payload.Length == 0 ? default! : responseCodec!.Deserialize(payload);
        }

        public TInput DeserializeRequest(SmithyHttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return default!;
            }

            var payload = GrpcMessageFraming.ReadSingle(request.Content ?? []);
            return payload.Length == 0 ? default! : requestCodec!.Deserialize(payload);
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
            return payload.Length == 0
                ? default!
                : ProtoCodec.FromSchema(errorSchema).Deserialize(payload);
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

    private static bool IsGrpcContentType(SmithyHttpResponse response) =>
        response.ContentHeaders.TryGetValue("Content-Type", out var contentType)
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
