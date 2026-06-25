namespace NSmithy.Http;

/// <summary>
/// Optional service-protocol extension for operations whose request or response is an event stream.
/// </summary>
public interface IEventStreamServiceProtocol
{
    IServerEventStreamOperationProtocol<TInput, TOutputEvent> ForServerEventStreamOperation<
        TInput,
        TOutput,
        TOutputEvent
    >(
        NSmithy.Core.Serde.OperationSchema<TInput, TOutput> operation,
        NSmithy.Core.Serde.Schema<TOutputEvent> outputEvent
    );

    IClientEventStreamOperationProtocol<TInputEvent, TOutput> ForClientEventStreamOperation<
        TInput,
        TInputEvent,
        TOutput
    >(
        NSmithy.Core.Serde.OperationSchema<TInput, TOutput> operation,
        NSmithy.Core.Serde.Schema<TInputEvent> inputEvent
    );

    IBidirectionalEventStreamOperationProtocol<
        TInputEvent,
        TOutputEvent
    > ForBidirectionalEventStreamOperation<TInput, TOutput, TInputEvent, TOutputEvent>(
        NSmithy.Core.Serde.OperationSchema<TInput, TOutput> operation,
        NSmithy.Core.Serde.Schema<TInputEvent> inputEvent,
        NSmithy.Core.Serde.Schema<TOutputEvent> outputEvent
    );
}

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
