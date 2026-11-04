using Example.Shop;

namespace Ownership.Orders;

/// <summary>Places orders. Knows nothing about the service that serves this operation.</summary>
public sealed class PlaceOrderHandler : IPlaceOrderHandler
{
    public Task<PlaceOrderOutput> PlaceOrderAsync(
        PlaceOrderInput input,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(new PlaceOrderOutput($"order-{input.Total.Amount}-{input.Total.Currency}"));
}
