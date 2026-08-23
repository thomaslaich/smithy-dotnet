using NSmithy.Core.Serde;
using NSmithy.Core.Validation;

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
    /// The protocol's default HTTP version preference. Generated clients replace this default
    /// with modeled <c>http</c> or <c>eventStreamHttp</c> preferences when present. A
    /// caller-supplied <see cref="System.Net.Http.HttpClient"/> is used as-is.
    /// </summary>
    SmithyHttpVersionPreference HttpVersionPreference => SmithyHttpVersionPreference.Http11;
}

/// <summary>
/// The client half of a protocol bound to a single (service, operation) pair: request
/// serialization, response deserialization, and error handling. Every protocol-specific wire
/// detail — URI scheme, framing, error discrimination — lives behind the implementation.
/// </summary>
public interface IClientOperationProtocol<TInput, TOutput>
{
    SmithyHttpRequest SerializeRequest(TInput input, CancellationToken cancellationToken = default);

    ValueTask<TOutput> DeserializeResponseAsync(
        SmithyHttpClientResponse response,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Decides whether a response represents an error, by the protocol's own rules — HTTP status
    /// for REST/rpcv2Cbor, the <c>grpc-status</c> trailer for gRPC. The client runtime uses this
    /// instead of assuming "4xx means error" so the transport stays protocol-agnostic.
    /// </summary>
    bool IsErrorResponse(SmithyHttpClientResponse response);

    /// <summary>
    /// Attempts to deserialize the response into one of the operation's modeled exceptions.
    /// Returns null when the protocol cannot resolve a modeled error from the response.
    /// The client runtime disposes a streaming response body after this returns (the error path
    /// abandons the response), so the returned exception must not retain the live stream.
    /// Implementations typically compose <see cref="OperationProtocolErrors"/> with their own
    /// discrimination rules.
    /// </summary>
    ValueTask<Exception?> DeserializeErrorAsync(
        SmithyHttpClientResponse response,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// The server half of a protocol bound to a single (service, operation) pair: request
/// deserialization and response/error serialization. The shared server runtime calls these; a host
/// adapter writes the returned <see cref="SmithyHttpServerResponse"/>.
/// </summary>
public interface IServerOperationProtocol<TInput, TOutput>
{
    /// <summary>
    /// Validates deserialized input against the model's constraint traits before the handler runs.
    /// Null when the input schema carries no constraints (or the protocol opts out); the server
    /// runtime then skips validation entirely. Compiled once per operation from the operation's
    /// input schema.
    /// </summary>
    /// <remarks>
    /// Deliberately not defaulted: an implementation that skips validation has to say so. A default
    /// of null once let six event-stream protocols silently validate nothing simply by not
    /// mentioning this member.
    /// </remarks>
    ISmithyValidator<TInput>? InputValidator { get; }

    ValueTask<TInput> DeserializeRequestAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    );

    SmithyHttpServerResponse SerializeResponse(
        TOutput output,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Serializes a modeled error to a protocol error response. The operation's modeled errors and
    /// their status codes come from the schema the protocol already holds, so the caller supplies
    /// only the thrown exception. Returns false for an exception that is not one of the operation's
    /// modeled errors, which the runtime rethrows (surfaced as a 500 by the host).
    /// </summary>
    bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response);
}

/// <summary>
/// A protocol bound to a single (service, operation) pair, usable from both call sides. Protocol
/// implementations implement this combined interface; client-side code (operation bindings, the
/// client runtime) depends only on <see cref="IClientOperationProtocol{TInput, TOutput}"/> and
/// server-side code only on <see cref="IServerOperationProtocol{TInput, TOutput}"/>. Streaming is
/// represented by event-stream members on <typeparamref name="TInput"/> or
/// <typeparamref name="TOutput"/>, not by a different protocol interface.
/// </summary>
public interface IOperationProtocol<TInput, TOutput>
    : IClientOperationProtocol<TInput, TOutput>,
        IServerOperationProtocol<TInput, TOutput>;
