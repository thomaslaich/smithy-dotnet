using NSmithy.Http;

namespace NSmithy.Server;

/// <summary>
/// The shared, host-agnostic server runtime: the dual of the client runtime's execution core. It
/// owns the dispatch algorithm — deserialize the request, invoke the handler, serialize the
/// response or a modeled error — for every protocol and operation shape, so generated code and host
/// adapters carry none of it. It takes a neutral <see cref="SmithyHttpRequest"/> and returns a
/// neutral <see cref="SmithyHttpServerResponse"/>; a host adapter owns conversion to and from the host
/// framework's types. Instance-based so server interceptors and telemetry can attach here later, as
/// they do on <c>SmithyClientRuntime</c>.
/// </summary>
public sealed class SmithyServerRuntime
{
    /// <summary>
    /// Unary dispatch: deserialize, invoke, serialize — with the operation's modeled errors caught
    /// once, for every protocol. An unmodeled exception rethrows (surfaced as a 500 by the host).
    /// </summary>
    public async Task<SmithyHttpServerResponse> DispatchAsync<TInput, TOutput>(
        IServerOperationProtocol<TInput, TOutput> protocol,
        SmithyHttpRequest request,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        var input = protocol.DeserializeRequest(request);
        try
        {
            var output = await handler(input, cancellationToken).ConfigureAwait(false);
            return protocol.SerializeResponse(output);
        }
        catch (Exception exception)
            when (protocol.TrySerializeError(exception, out var errorResponse))
        {
            return errorResponse;
        }
    }

    /// <summary>Output-stream dispatch: unary request in, events out.</summary>
    public SmithyHttpServerResponse DispatchOutputStream<TInput, TOutputEvent>(
        IOutputEventStreamServerProtocol<TInput, TOutputEvent> protocol,
        SmithyHttpRequest request,
        Func<TInput, CancellationToken, IAsyncEnumerable<TOutputEvent>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        var input = protocol.DeserializeRequest(request);
        return protocol.SerializeResponse(handler(input, cancellationToken), cancellationToken);
    }

    /// <summary>Input-stream dispatch: events in, unary response out.</summary>
    public async Task<SmithyHttpServerResponse> DispatchInputStreamAsync<TInputEvent, TOutput>(
        IInputEventStreamServerProtocol<TInputEvent, TOutput> protocol,
        SmithyHttpRequest request,
        Func<IAsyncEnumerable<TInputEvent>, CancellationToken, Task<TOutput>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        var input = protocol.DeserializeRequestEventsAsync(request, cancellationToken);
        var output = await handler(input, cancellationToken).ConfigureAwait(false);
        return protocol.SerializeResponse(output);
    }

    /// <summary>Duplex-stream dispatch: events in both directions.</summary>
    public SmithyHttpServerResponse DispatchDuplexStream<TInputEvent, TOutputEvent>(
        IDuplexEventStreamServerProtocol<TInputEvent, TOutputEvent> protocol,
        SmithyHttpRequest request,
        Func<
            IAsyncEnumerable<TInputEvent>,
            CancellationToken,
            IAsyncEnumerable<TOutputEvent>
        > handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        var input = protocol.DeserializeRequestEventsAsync(request, cancellationToken);
        return protocol.SerializeResponse(handler(input, cancellationToken), cancellationToken);
    }
}
