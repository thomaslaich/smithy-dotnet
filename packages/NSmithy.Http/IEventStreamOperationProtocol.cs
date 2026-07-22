namespace NSmithy.Http;

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
// SmithyServerResponse whose body is framed chunks. Transports and hosts stay protocol-neutral.

/// <summary>Client half of an output-stream operation: unary request in, events out.</summary>
public interface IOutputEventStreamClientProtocol<TInput, TOutputEvent>
{
    SmithyHttpRequest SerializeRequest(TInput input);

    /// <summary>
    /// Deframes and decodes the response events. Implementations own <paramref name="response"/>'s
    /// body stream and must dispose it when enumeration completes or is abandoned.
    /// </summary>
    IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
        SmithyStreamingHttpResponse response,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Server half of an output-stream operation: unary request in, framed events out.</summary>
public interface IOutputEventStreamServerProtocol<TInput, TOutputEvent>
{
    TInput DeserializeRequest(SmithyHttpRequest request);

    /// <summary>Encodes and frames the response events into a streamed server response.</summary>
    SmithyServerResponse SerializeResponse(
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
        SmithyStreamingHttpResponse response,
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
    SmithyServerResponse SerializeResponse(TOutput output);
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
        SmithyStreamingHttpResponse response,
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
    SmithyServerResponse SerializeResponse(
        IAsyncEnumerable<TOutputEvent> output,
        CancellationToken cancellationToken = default
    );
}

/// <summary>A duplex-stream operation protocol usable from both call sides.</summary>
public interface IDuplexEventStreamOperationProtocol<TInputEvent, TOutputEvent>
    : IDuplexEventStreamClientProtocol<TInputEvent, TOutputEvent>,
        IDuplexEventStreamServerProtocol<TInputEvent, TOutputEvent>;
