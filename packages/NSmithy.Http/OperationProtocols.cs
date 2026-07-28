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

    TOutput DeserializeResponse(SmithyHttpClientResponse response);

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
    TInput DeserializeRequest(SmithyHttpRequest request);

    SmithyHttpServerResponse SerializeResponse(TOutput output);

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
/// server-side code only on <see cref="IServerOperationProtocol{TInput, TOutput}"/>. This is the
/// <em>unary</em> shape — streaming variants are separate interfaces.
/// </summary>
public interface IOperationProtocol<TInput, TOutput>
    : IClientOperationProtocol<TInput, TOutput>,
        IServerOperationProtocol<TInput, TOutput>;

// Event-stream operation protocols, named by stream direction × call side.
//
// Direction is the @streaming member's position in the model:
//   - Output: the response carries the event stream (server pushes events).
//   - Input: the request carries the event stream (client pushes events).
//   - Duplex: both.
//
// Each direction has a client half and a server half; protocol implementations implement the
// combined interface. The protocol owns all wire framing: client halves emit a fully framed
// request body (a SmithyHttpBody.EventStreaming, or Bytes for a unary output-stream request) and
// deframe the streaming response; server halves deframe the raw request body and emit a
// SmithyHttpServerResponse whose body is framed chunks. Transports and hosts stay protocol-neutral.

/// <summary>Client half of an output-stream operation: unary request in, events out.</summary>
public interface IOutputEventStreamClientProtocol<TInput, TOutputEvent>
{
    SmithyHttpRequest SerializeRequest(TInput input);

    /// <summary>
    /// Deframes and decodes the response events. Implementations own <paramref name="response"/>'s
    /// body stream and must dispose it when enumeration completes or is abandoned.
    /// </summary>
    IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
        SmithyHttpClientResponse response,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Server half of an output-stream operation: unary request in, framed events out.</summary>
public interface IOutputEventStreamServerProtocol<TInput, TOutputEvent>
{
    TInput DeserializeRequest(SmithyHttpRequest request);

    /// <summary>Encodes and frames the response events into a streamed server response.</summary>
    SmithyHttpServerResponse SerializeResponse(
        IAsyncEnumerable<TOutputEvent> output,
        CancellationToken cancellationToken = default
    );
}

/// <summary>An output-stream operation protocol usable from both call sides.</summary>
public interface IOutputEventStreamOperationProtocol<TInput, TOutputEvent>
    : IOutputEventStreamClientProtocol<TInput, TOutputEvent>,
        IOutputEventStreamServerProtocol<TInput, TOutputEvent>;

/// <summary>Client half of an input-stream operation: events in, unary response out.</summary>
public interface IInputEventStreamClientProtocol<TInputEvent, TOutput>
{
    SmithyHttpRequest SerializeRequest(
        IAsyncEnumerable<TInputEvent> input,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the unary response. Implementations own <paramref name="response"/>'s body stream
    /// and must dispose it before returning.
    /// </summary>
    ValueTask<TOutput> DeserializeResponseAsync(
        SmithyHttpClientResponse response,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Server half of an input-stream operation: framed events in, unary response out.</summary>
public interface IInputEventStreamServerProtocol<TInputEvent, TOutput>
{
    /// <summary>Deframes and decodes the request events from the raw request body.</summary>
    IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Encodes and frames the unary response into a server response.</summary>
    SmithyHttpServerResponse SerializeResponse(TOutput output);
}

/// <summary>An input-stream operation protocol usable from both call sides.</summary>
public interface IInputEventStreamOperationProtocol<TInputEvent, TOutput>
    : IInputEventStreamClientProtocol<TInputEvent, TOutput>,
        IInputEventStreamServerProtocol<TInputEvent, TOutput>;

/// <summary>Client half of a duplex-stream operation: events in both directions.</summary>
public interface IDuplexEventStreamClientProtocol<TInputEvent, TOutputEvent>
{
    SmithyHttpRequest SerializeRequest(
        IAsyncEnumerable<TInputEvent> input,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deframes and decodes the response events. Implementations own <paramref name="response"/>'s
    /// body stream and must dispose it when enumeration completes or is abandoned.
    /// </summary>
    IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
        SmithyHttpClientResponse response,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Server half of a duplex-stream operation: events in both directions.</summary>
public interface IDuplexEventStreamServerProtocol<TInputEvent, TOutputEvent>
{
    /// <summary>Deframes and decodes the request events from the raw request body.</summary>
    IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Encodes and frames the response events into a streamed server response.</summary>
    SmithyHttpServerResponse SerializeResponse(
        IAsyncEnumerable<TOutputEvent> output,
        CancellationToken cancellationToken = default
    );
}

/// <summary>A duplex-stream operation protocol usable from both call sides.</summary>
public interface IDuplexEventStreamOperationProtocol<TInputEvent, TOutputEvent>
    : IDuplexEventStreamClientProtocol<TInputEvent, TOutputEvent>,
        IDuplexEventStreamServerProtocol<TInputEvent, TOutputEvent>;
