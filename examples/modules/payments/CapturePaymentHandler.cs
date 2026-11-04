using Example.Shop;

namespace NSmithy.Examples.Modules.Payments;

/// <summary>Captures payments, failing with the shared OrderNotFound error.</summary>
public sealed class CapturePaymentHandler : ICapturePaymentHandler
{
    public Task<CapturePaymentOutput> CapturePaymentAsync(
        CapturePaymentInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (!input.OrderId.StartsWith("order-", StringComparison.Ordinal))
        {
            throw new OrderNotFound($"No order {input.OrderId}.");
        }
        return Task.FromResult(new CapturePaymentOutput($"payment-for-{input.OrderId}"));
    }
}
