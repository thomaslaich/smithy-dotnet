namespace NSmithy.Messaging;

/// <summary>Immutable operation facts and a reusable encoder; contains no transport state.</summary>
public class MessageSendBinding<T>(
    string serviceId,
    string operationId,
    string address,
    Func<T, MessagePayload> encode
)
{
    public string ServiceId { get; } = serviceId;
    public string OperationId { get; } = operationId;
    public string Address { get; } = address;
    public Func<T, MessagePayload> Encode { get; } = encode;
}

public interface IMessageSender
{
    Task SendAsync<T>(
        MessageSendBinding<T> binding,
        T message,
        CancellationToken cancellationToken = default
    );
}
