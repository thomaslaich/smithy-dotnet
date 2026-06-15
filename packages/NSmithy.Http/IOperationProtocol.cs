using NSmithy.Core.Serde;

namespace NSmithy.Http;

/// <summary>
/// A protocol bound to a single service. Produced from a <see cref="ServiceSchema"/>; hands out
/// per-operation protocols. Service-level concerns (e.g. deriving the rpcv2Cbor request path from
/// the service shape name, and — in future — auth and endpoint resolution) live here, set up once.
/// </summary>
public interface IServiceProtocol
{
    IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    );
}

/// <summary>
/// A protocol bound to a single (service, operation) pair. The generated client and server call
/// these methods uniformly; every protocol-specific wire detail (URI scheme, framing, error
/// discrimination) lives behind the implementation. This is the <em>unary</em> shape — a streaming
/// sibling would be a separate interface.
/// </summary>
public interface IOperationProtocol<TInput, TOutput>
{
    // ---- client ----
    SmithyHttpRequest SerializeRequest(TInput input);
    TOutput DeserializeResponse(SmithyHttpResponse response);

    // ---- server ----
    TInput DeserializeRequest(SmithyHttpRequest request);
    SmithyHttpResponse SerializeResponse(TOutput output);

    // ---- errors ----
    // The *set* of an operation's errors is model data, so dispatch stays generated; the
    // protocol-specific mechanism (when a response is an error, header vs __type, status fallback)
    // is hidden here.

    /// <summary>
    /// Decides whether a response represents an error, by the protocol's own rules — HTTP status
    /// for REST/rpcv2Cbor, the <c>grpc-status</c> trailer for gRPC. The client runtime uses this
    /// instead of assuming "4xx means error" so the transport stays protocol-agnostic.
    /// </summary>
    bool IsErrorResponse(SmithyHttpResponse response);

    /// <summary>Returns the error type discriminator, or null when the response is not an error.</summary>
    string? GetErrorDiscriminator(SmithyHttpResponse response);

    TError DeserializeError<TError>(Schema<TError> errorSchema, SmithyHttpResponse response);

    SmithyHttpResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError value,
        string errorShapeId,
        int statusCode
    );
}
