using Example.Weather;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
}
