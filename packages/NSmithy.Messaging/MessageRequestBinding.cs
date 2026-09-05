using Microsoft.Extensions.DependencyInjection;

namespace NSmithy.Messaging;

public class MessageRequestBinding<TRequest, TReply>(
    string serviceId,
    string operationId,
    string address,
    Func<TRequest, MessagePayload> encode,
    Func<MessagePayload, TReply> decodeReply
) : MessageSendBinding<TRequest>(serviceId, operationId, address, encode)
{
    public Func<MessagePayload, TReply> DecodeReply { get; } = decodeReply;
}

public interface IMessageRequestSender
{
    Task<TReply> RequestAsync<TRequest, TReply>(
        MessageRequestBinding<TRequest, TReply> binding,
        TRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Typed request dispatch. The transport publishes the returned reply before settlement.</summary>
public sealed class MessageReplyReceiveBinding<TRequest, TReply, THandler>(
    string serviceId,
    string operationId,
    string address,
    Func<MessagePayload, TRequest> decode,
    Func<TReply, MessagePayload> encodeReply,
    Func<THandler, TRequest, CancellationToken, Task<TReply>> handle
) : MessageReceiveBinding(serviceId, operationId, address)
    where THandler : notnull
{
    public override bool HasReply => true;

    public override void Validate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(THandler)))
            throw new InvalidOperationException(
                $"Operation {OperationId} requires a {typeof(THandler).Name} registration."
            );
    }

    public override async Task<MessagePayload?> DispatchAsync(
        MessagePayload payload,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        var request = decode(payload);
        var reply = await handle(
                services.GetRequiredService<THandler>(),
                request,
                cancellationToken
            )
            .ConfigureAwait(false);
        return encodeReply(reply);
    }
}
