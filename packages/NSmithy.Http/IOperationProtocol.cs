using NSmithy.Core.Serde;

namespace NSmithy.Http;

/// <summary>
/// An unbound protocol: a wire format (REST/JSON, rpcv2Cbor, gRPC, …) before it is tied to a
/// particular service. A single instance is passed to a client builder, which binds it to the
/// service via <see cref="ForService"/>. Implementations are instantiable so they can carry
/// protocol-specific configuration (e.g. gRPC message-size limits) as instance state.
/// </summary>
public interface IProtocol
{
    IServiceProtocol ForService(ServiceSchema service);

    /// <summary>
    /// Whether this protocol requires an HTTP/2 transport. The generated client uses this to
    /// configure the default <see cref="System.Net.Http.HttpClient"/> it creates when the caller
    /// doesn't supply one — native gRPC needs HTTP/2; REST and rpcv2Cbor run on HTTP/1.1. A
    /// caller-supplied <c>HttpClient</c> is used as-is and must be configured for the protocol.
    /// </summary>
    bool RequiresHttp2 => false;
}

/// <summary>
/// The client half of a protocol bound to a single (service, operation) pair: request
/// serialization, response deserialization, and error handling. Every protocol-specific wire
/// detail — URI scheme, framing, error discrimination — lives behind the implementation.
/// </summary>
public interface IClientOperationProtocol<TInput, TOutput>
{
    SmithyHttpRequest SerializeRequest(TInput input);

    TOutput DeserializeResponse(SmithyHttpResponse response);

    /// <summary>
    /// Decides whether a response represents an error, by the protocol's own rules — HTTP status
    /// for REST/rpcv2Cbor, the <c>grpc-status</c> trailer for gRPC. The client runtime uses this
    /// instead of assuming "4xx means error" so the transport stays protocol-agnostic.
    /// </summary>
    bool IsErrorResponse(SmithyHttpResponse response);

    /// <summary>
    /// Attempts to deserialize the response into one of the operation's modeled exceptions.
    /// Returns null when the protocol cannot resolve a modeled error from the response.
    /// The client runtime disposes a streaming response body after this returns (the error path
    /// abandons the response), so the returned exception must not retain the live stream.
    /// Implementations typically compose <see cref="OperationProtocolErrors"/> with their own
    /// discrimination rules.
    /// </summary>
    ValueTask<Exception?> DeserializeErrorAsync(
        SmithyHttpResponse response,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// The server half of a protocol bound to a single (service, operation) pair: request
/// deserialization and response/error serialization. Generated ASP.NET Core handlers call these.
/// </summary>
public interface IServerOperationProtocol<TInput, TOutput>
{
    TInput DeserializeRequest(SmithyHttpRequest request);

    SmithyHttpResponse SerializeResponse(TOutput output);

    SmithyHttpResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError value,
        string errorShapeId,
        int statusCode
    );
}

/// <summary>
/// A protocol bound to a single (service, operation) pair, usable from both call sides. Protocol
/// implementations implement this combined interface; client-side code (operation bindings, the
/// client runtime) depends only on <see cref="IClientOperationProtocol{TInput, TOutput}"/> and
/// server-side code only on <see cref="IServerOperationProtocol{TInput, TOutput}"/>. This is the
/// <em>unary</em> shape — streaming variants are separate interfaces.
/// </summary>
public interface IOperationProtocol<TInput, TOutput>
    : IClientOperationProtocol<TInput, TOutput>,
        IServerOperationProtocol<TInput, TOutput>;
