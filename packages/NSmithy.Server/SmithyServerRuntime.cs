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
    /// Dispatches an operation: deserialize, invoke, serialize — with the operation's modeled
    /// errors caught once, for every protocol. An unmodeled exception rethrows (surfaced as a 500
    /// by the host).
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

        var input = await protocol
            .DeserializeRequestAsync(request, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var output = await handler(input, cancellationToken).ConfigureAwait(false);
            return protocol.SerializeResponse(output, cancellationToken);
        }
        catch (Exception exception)
            when (protocol.TrySerializeError(exception, out var errorResponse))
        {
            return errorResponse;
        }
    }
}
