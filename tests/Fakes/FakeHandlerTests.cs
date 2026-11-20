using Example.Weather;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Core;
using NSmithy.Server;
using NSmithy.Server.AspNetCore;

namespace NSmithy.Fakes.Tests;

public class FakeHandlerTests
{
    [Fact]
    public async Task RealClientTalksToAServerBootedFromTheFakeHandler()
    {
        // A working in-process server with no handler implementation: the fake
        // handler serves the @examples data over the real restJson1 wire.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var resolutions = 0;
        builder.Services.AddScoped(_ =>
        {
            resolutions++;
            return new SmithyServerRuntime();
        });
        builder.Services.AddWeatherServiceHandler<FakeWeatherServiceHandler>();
        await using var app = builder.Build();
        app.MapWeatherService();
        await app.StartAsync();

        using var client = new WeatherClient(app.GetTestClient());

        var city = await client.GetCityAsync(new GetCityInput("SEA"));

        Assert.Equal("Seattle", city.Name);
        Assert.Equal(47.6f, city.Coordinates.Latitude);

        // The fake handler matches the input against the @examples inputs, so
        // each example's data (including its error example) survives the round
        // trip over the wire.
        var houston = await client.GetCityAsync(new GetCityInput("HOU"));
        Assert.Equal("Houston", houston.Name);

        var error = await Assert.ThrowsAsync<NoSuchCity>(() =>
            client.GetCityAsync(new GetCityInput("UNK"))
        );
        Assert.Equal("no city with ID UNK", error.Message);

        Assert.Equal(3, resolutions);
        await app.StopAsync();
    }

    [Fact]
    public void DefaultRuntimeRegistrationIsIdempotentAndPreservesApplicationRuntime()
    {
        var services = new ServiceCollection();
        services.AddSmithyServer();
        services.AddSmithyServer();
        Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(SmithyServerRuntime)
        );
        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<SmithyServerRuntime>(),
            provider.GetRequiredService<SmithyServerRuntime>()
        );

        var custom = new SmithyServerRuntime();
        var customized = new ServiceCollection();
        customized.AddSingleton(custom);
        customized.AddWeatherServiceHandler<FakeWeatherServiceHandler>();
        using var customProvider = customized.BuildServiceProvider();
        Assert.Same(custom, customProvider.GetRequiredService<SmithyServerRuntime>());
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void HandlerRegistrationUsesTheGivenLifetime(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddWeatherServiceHandler<FakeWeatherServiceHandler>(lifetime);

        var handler = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IWeatherServiceHandler)
        );
        Assert.Equal(lifetime, handler.Lifetime);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IGetCityHandler)
        );
    }

    [Fact]
    public void HandlerRegistrationDefaultsToASingleton()
    {
        var services = new ServiceCollection();
        services.AddWeatherServiceHandler<FakeWeatherServiceHandler>();

        Assert.Equal(
            ServiceLifetime.Singleton,
            services
                .Single(descriptor => descriptor.ServiceType == typeof(IWeatherServiceHandler))
                .Lifetime
        );
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    public async Task AHandlerServingSeveralOperationsIsCreatedAndDisposedOnce(
        ServiceLifetime lifetime
    )
    {
        var counter = new Counter();
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddWeatherServiceHandler<DisposableWeatherHandler>(lifetime);
        var operations = new WeatherServiceDefinition().Operations;

        await using (var provider = services.BuildServiceProvider(validateScopes: true))
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                await operations
                    .GetOperation(new ShapeId("example.weather", "GetCity"))
                    .InvokeAsync(
                        SmithyServerRuntime.Default,
                        new GetCityInput("SEA"),
                        scope.ServiceProvider
                    );
                await operations
                    .GetOperation(new ShapeId("example.weather", "ListCities"))
                    .InvokeAsync(
                        SmithyServerRuntime.Default,
                        new ListCitiesInput(),
                        scope.ServiceProvider
                    );
            }

            Assert.Equal(1, counter.Created);
            Assert.Equal(lifetime == ServiceLifetime.Scoped ? 1 : 0, counter.Disposed);
        }

        Assert.Equal(1, counter.Disposed);
    }

    [Fact]
    public async Task AnOperationsOwnHandlerTakesPrecedence()
    {
        var services = new ServiceCollection();
        services.AddWeatherServiceHandler<FakeWeatherServiceHandler>();
        services.AddSingleton<IGetCityHandler, AnywhereIsSeattle>();
        await using var provider = services.BuildServiceProvider();

        var output = await new WeatherServiceDefinition()
            .Operations.GetOperation(new ShapeId("example.weather", "GetCity"))
            .InvokeAsync(SmithyServerRuntime.Default, new GetCityInput("HOU"), provider);

        Assert.Equal("Seattle", Assert.IsType<GetCityOutput>(output).Name);
    }

    private sealed class Counter
    {
        public int Created { get; set; }

        public int Disposed { get; set; }
    }

    private sealed class DisposableWeatherHandler : FakeWeatherServiceHandler, IDisposable
    {
        private readonly Counter counter;

        public DisposableWeatherHandler(Counter counter)
        {
            this.counter = counter;
            counter.Created++;
        }

        public void Dispose() => counter.Disposed++;
    }

    private sealed class AnywhereIsSeattle : IGetCityHandler
    {
        public Task<GetCityOutput> GetCityAsync(
            GetCityInput input,
            CancellationToken cancellationToken = default
        ) =>
            new FakeWeatherServiceHandler().GetCityAsync(
                new GetCityInput("SEA"),
                cancellationToken
            );
    }
}
