using Example.Shop;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Core.Validation;
using Ownership.Orders;
using Ownership.Payments;

namespace Ownership.Tests;

/// <summary>
/// A service whose operations are owned and implemented by other projects: the Api project
/// generates only the service, and routes to the handlers the modules generated and implemented.
/// </summary>
public sealed class ModulesTests : IAsyncLifetime
{
    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddShopService();
        builder.Services.AddSingleton<IPlaceOrderHandler, PlaceOrderHandler>();
        builder.Services.AddSingleton<ICapturePaymentHandler, CapturePaymentHandler>();
        _app = builder.Build();
        _app.MapShopService();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private ShopClient Client => new(_app!.GetTestClient());

    [Fact]
    public async Task RoutesToTheHandlerOfTheModuleThatOwnsTheOperation()
    {
        using var client = Client;

        var order = await client.PlaceOrderAsync(new PlaceOrderInput(new Money(42, "CHF")));
        var payment = await client.CapturePaymentAsync(
            new CapturePaymentInput(new Money(42, "CHF"), order.OrderId)
        );

        Assert.Equal("order-42-CHF", order.OrderId);
        Assert.Equal("payment-for-order-42-CHF", payment.PaymentId);
    }

    [Fact]
    public async Task AProvidedErrorRoundTripsAsItsSharedType()
    {
        using var client = Client;

        var error = await Assert.ThrowsAsync<OrderNotFound>(() =>
            client.CapturePaymentAsync(new CapturePaymentInput(new Money(1, "CHF"), "nope"))
        );

        Assert.Equal("No order nope.", error.Message);
    }

    [Fact]
    public async Task AProvidedShapesConstraintsAreValidatedInTheService()
    {
        using var client = Client;

        await Assert.ThrowsAsync<ValidationException>(() =>
            client.PlaceOrderAsync(new PlaceOrderInput(new Money(0, "CHF")))
        );
    }
}
