using Microsoft.Extensions.DependencyInjection;

namespace NSmithy.Messaging;

/// <summary>Operation facts and typed dispatch. Successful completion permits settlement.</summary>
public abstract class MessageReceiveBinding(string serviceId, string operationId, string address)
{
    public string ServiceId { get; } = serviceId;
    public string OperationId { get; } = operationId;
    public string Address { get; } = address;
    public virtual bool HasReply => false;
    public abstract void Validate(IServiceProvider services);
    public abstract Task<MessagePayload?> DispatchAsync(
        MessagePayload payload,
        IServiceProvider services,
        CancellationToken cancellationToken
    );
}

public sealed class MessageReceiveBinding<T, THandler>(
    string serviceId,
    string operationId,
    string address,
    Func<MessagePayload, T> decode,
    Func<THandler, T, CancellationToken, Task> handle
) : MessageReceiveBinding(serviceId, operationId, address)
    where THandler : notnull
{
    public override void Validate(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Validate without constructing a scoped handler in the root container.
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
        var message = decode(payload);
        await handle(services.GetRequiredService<THandler>(), message, cancellationToken)
            .ConfigureAwait(false);
        return null;
    }
}
