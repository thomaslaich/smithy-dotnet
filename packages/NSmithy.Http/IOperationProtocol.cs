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

    /// <summary>
    /// Decides whether a response represents an error, by the protocol's own rules — HTTP status
    /// for REST/rpcv2Cbor, the <c>grpc-status</c> trailer for gRPC. The client runtime uses this
    /// instead of assuming "4xx means error" so the transport stays protocol-agnostic.
    /// </summary>
    bool IsErrorResponse(SmithyHttpResponse response);

    /// <summary>Returns the error type discriminator, or null when the response is not an error.</summary>
    string? GetErrorDiscriminator(SmithyHttpResponse response);

    /// <summary>
    /// When true, a response whose <see cref="GetErrorDiscriminator"/> yields <c>null</c> is treated
    /// as carrying no modeled error (the client returns no exception). True for rpc-style protocols
    /// (rpcv2Cbor, gRPC) whose errors always carry an explicit discriminator; false for REST, which
    /// can still resolve an error from the HTTP status code.
    /// </summary>
    bool RequiresErrorDiscriminator { get; }

    /// <summary>
    /// When true, the client may fall back to the HTTP status code to identify a modeled error when
    /// the discriminator did not resolve. True for REST; false for rpc-style protocols where the
    /// HTTP status does not map to a specific error shape (gRPC always returns HTTP 200).
    /// </summary>
    bool SupportsHttpStatusErrorFallback { get; }

    TError DeserializeError<TError>(Schema<TError> errorSchema, SmithyHttpResponse response);

    IReadOnlyList<IOperationErrorSchema> ModeledErrors => [];

    /// <summary>
    /// Attempts to deserialize the response into one of the operation's modeled exceptions.
    /// Returns null when the protocol cannot resolve a modeled error from the response.
    /// </summary>
    ValueTask<Exception?> DeserializeErrorAsync(
        SmithyHttpResponse response,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(OperationProtocolErrors.DeserializeModeledError(this, response));

    SmithyHttpResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError value,
        string errorShapeId,
        int statusCode
    );
}
