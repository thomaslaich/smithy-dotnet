using Microsoft.Extensions.DependencyInjection;

namespace NSmithy.Messaging;

/// <summary>Owns a DI scope for the complete delivery, including asynchronous scope disposal.</summary>
public sealed class MessageProcessor(IServiceScopeFactory scopes)
{
    public async Task<MessagePayload?> ProcessAsync(
        MessageReceiveBinding binding,
        MessagePayload payload,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopes.CreateAsyncScope();
        return await binding
            .DispatchAsync(payload, scope.ServiceProvider, cancellationToken)
            .ConfigureAwait(false);
    }
}
