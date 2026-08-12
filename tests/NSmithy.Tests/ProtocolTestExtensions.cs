using NSmithy.Core.Serde;
using NSmithy.Http;

internal static class ProtocolTestExtensions
{
    public static SmithyHttpRequest SerializeRequest<TInput, TOutput>(
        this IClientOperationProtocol<TInput, TOutput> protocol,
        TInput input
    ) => protocol.SerializeRequest(input);

    public static TOutput DeserializeResponse<TInput, TOutput>(
        this IClientOperationProtocol<TInput, TOutput> protocol,
        SmithyHttpClientResponse response
    ) => protocol.DeserializeResponseAsync(response).AsTask().GetAwaiter().GetResult();

    public static TInput DeserializeRequest<TInput, TOutput>(
        this IServerOperationProtocol<TInput, TOutput> protocol,
        SmithyHttpRequest request
    ) => protocol.DeserializeRequestAsync(request).AsTask().GetAwaiter().GetResult();

    public static SmithyHttpServerResponse SerializeResponse<TInput, TOutput>(
        this IServerOperationProtocol<TInput, TOutput> protocol,
        TOutput output
    ) => protocol.SerializeResponse(output);

    /// <summary>
    /// Both halves from one instance. Production code takes one side or the other from
    /// <see cref="IServiceProtocol"/> so neither compiles the other's work, but a round-trip test
    /// wants a single object — and every implementation does answer both. Built as the server so
    /// the input validator is present.
    /// </summary>
    public static IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
        this IServiceProtocol protocol,
        OperationSchema<TInput, TOutput> operation
    ) => (IOperationProtocol<TInput, TOutput>)protocol.ForServerOperation(operation);

    public static IOperationProtocol<TInput, TOutput> ForOutputEventStreamOperation<
        TInput,
        TOutput,
        TOutputEvent
    >(
        this IServiceProtocol protocol,
        OperationSchema<TInput, TOutput> operation,
        Schema<TOutputEvent> outputEvent
    ) => protocol.ForOperation(operation);

    public static IOperationProtocol<TInput, TOutput> ForInputEventStreamOperation<
        TInput,
        TInputEvent,
        TOutput
    >(
        this IServiceProtocol protocol,
        OperationSchema<TInput, TOutput> operation,
        Schema<TInputEvent> inputEvent
    ) => protocol.ForOperation(operation);

    public static IOperationProtocol<TInput, TOutput> ForDuplexEventStreamOperation<
        TInput,
        TOutput,
        TInputEvent,
        TOutputEvent
    >(
        this IServiceProtocol protocol,
        OperationSchema<TInput, TOutput> operation,
        Schema<TInputEvent> inputEvent,
        Schema<TOutputEvent> outputEvent
    ) => protocol.ForOperation(operation);
}
