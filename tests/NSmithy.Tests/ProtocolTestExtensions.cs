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
