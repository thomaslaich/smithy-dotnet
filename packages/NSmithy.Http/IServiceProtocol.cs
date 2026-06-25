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

    IServerEventStreamOperationProtocol<TInput, TOutputEvent> ForServerEventStreamOperation<
        TInput,
        TOutput,
        TOutputEvent
    >(OperationSchema<TInput, TOutput> operation, Schema<TOutputEvent> outputEvent) =>
        throw new NotSupportedException("This protocol does not support server event streaming.");

    IClientEventStreamOperationProtocol<TInputEvent, TOutput> ForClientEventStreamOperation<
        TInput,
        TInputEvent,
        TOutput
    >(OperationSchema<TInput, TOutput> operation, Schema<TInputEvent> inputEvent) =>
        throw new NotSupportedException("This protocol does not support client event streaming.");

    IBidirectionalEventStreamOperationProtocol<
        TInputEvent,
        TOutputEvent
    > ForBidirectionalEventStreamOperation<TInput, TOutput, TInputEvent, TOutputEvent>(
        OperationSchema<TInput, TOutput> operation,
        Schema<TInputEvent> inputEvent,
        Schema<TOutputEvent> outputEvent
    ) =>
        throw new NotSupportedException(
            "This protocol does not support bidirectional event streaming."
        );
}
