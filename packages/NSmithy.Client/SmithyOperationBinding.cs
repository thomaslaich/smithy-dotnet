using NSmithy.Http;

namespace NSmithy.Client;

public sealed class SmithyOperationBinding<TInput, TOutput>
{
    public SmithyOperationBinding(
        string serviceName,
        string operationName,
        IOperationProtocol<TInput, TOutput> protocol,
        Action<SmithyHttpRequest>? modifyRequest = null,
        SmithyErrorDeserializer? errorDeserializer = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(protocol);

        ServiceName = serviceName;
        OperationName = operationName;
        Protocol = protocol;
        ModifyRequest = modifyRequest;
        ErrorDeserializer = errorDeserializer;
    }

    public string ServiceName { get; }

    public string OperationName { get; }

    public IOperationProtocol<TInput, TOutput> Protocol { get; }

    public Action<SmithyHttpRequest>? ModifyRequest { get; }

    public SmithyErrorDeserializer? ErrorDeserializer { get; }
}
