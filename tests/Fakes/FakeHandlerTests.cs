using Example.Weather;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        builder.Services.AddWeatherServiceHandler<FakeWeatherServiceHandler>();
        await using var app = builder.Build();
        app.MapWeatherService();
        await app.StartAsync();

        using var client = new WeatherClient(app.GetTestClient());

        var city = await client.GetCityAsync(new GetCityInput("SEA"));

        Assert.Equal("Seattle", city.Name);
        Assert.Equal(47.6f, city.Coordinates.Latitude);

        await app.StopAsync();
    }
}
