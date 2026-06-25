using NSmithy.Core.Serde;

namespace NSmithy.Http;

public interface IServerEventStreamOperationProtocol<TInput, TOutputEvent>
{
    SmithyEventStreamHttpRequest SerializeRequest(TInput input);

    TInput DeserializeRequest(SmithyHttpRequest request);

    IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
        SmithyEventStreamHttpResponse response,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<SmithyEventFrame> SerializeResponseEventsAsync(
        IAsyncEnumerable<TOutputEvent> output,
        CancellationToken cancellationToken = default
    );
}

public interface IClientEventStreamOperationProtocol<TInputEvent, TOutput>
{
    SmithyEventStreamHttpRequest SerializeRequest(
        IAsyncEnumerable<TInputEvent> input,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
        SmithyEventStreamHttpRequest request,
        CancellationToken cancellationToken = default
    );

    ValueTask<TOutput> DeserializeResponseAsync(
        SmithyEventStreamHttpResponse response,
        CancellationToken cancellationToken = default
    );

    SmithyEventFrame SerializeResponse(TOutput output);
}

public interface IBidirectionalEventStreamOperationProtocol<TInputEvent, TOutputEvent>
{
    SmithyEventStreamHttpRequest SerializeRequest(
        IAsyncEnumerable<TInputEvent> input,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<TInputEvent> DeserializeRequestEventsAsync(
        SmithyEventStreamHttpRequest request,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<TOutputEvent> DeserializeResponseEventsAsync(
        SmithyEventStreamHttpResponse response,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<SmithyEventFrame> SerializeResponseEventsAsync(
        IAsyncEnumerable<TOutputEvent> output,
        CancellationToken cancellationToken = default
    );
}
