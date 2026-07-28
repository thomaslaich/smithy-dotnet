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

    IOutputEventStreamOperationProtocol<TInput, TOutputEvent> ForOutputEventStreamOperation<
        TInput,
        TOutput,
        TOutputEvent
    >(OperationSchema<TInput, TOutput> operation, Schema<TOutputEvent> outputEvent) =>
        throw new NotSupportedException("This protocol does not support output event streaming.");

    IInputEventStreamOperationProtocol<TInputEvent, TOutput> ForInputEventStreamOperation<
        TInput,
        TInputEvent,
        TOutput
    >(OperationSchema<TInput, TOutput> operation, Schema<TInputEvent> inputEvent) =>
        throw new NotSupportedException("This protocol does not support input event streaming.");

    IDuplexEventStreamOperationProtocol<TInputEvent, TOutputEvent> ForDuplexEventStreamOperation<
        TInput,
        TOutput,
        TInputEvent,
        TOutputEvent
    >(
        OperationSchema<TInput, TOutput> operation,
        Schema<TInputEvent> inputEvent,
        Schema<TOutputEvent> outputEvent
    ) => throw new NotSupportedException("This protocol does not support duplex event streaming.");
}
