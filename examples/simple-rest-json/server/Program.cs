using Example.Weather;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<WeatherHandler>();

var app = builder.Build();
app.MapWeatherServiceHttp();
app.Run();

internal sealed class WeatherHandler : IWeatherServiceHandler
{
    public Task<GetCurrentTimeOutput> GetCurrentTimeAsync(
        GetCurrentTimeInput input,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(new GetCurrentTimeOutput(DateTimeOffset.UtcNow));
    }

    public Task<GetCityOutput> GetCityAsync(
        GetCityInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (input.CityId == "unknown")
            throw new NoSuchResource(null, "City");

        return Task.FromResult(
            new GetCityOutput(name: "Seattle", coordinates: new CityCoordinates(47.6f, -122.3f))
        );
    }

    public Task<ListCitiesOutput> ListCitiesAsync(
        ListCitiesInput input,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(
            new ListCitiesOutput(
                new CitySummaries([
                    new CitySummary("SEA", "Seattle"),
                    new CitySummary("NYC", "New York"),
                    new CitySummary("LAX", "Los Angeles"),
                ])
            )
        );
    }

    public Task<GetForecastOutput> GetForecastAsync(
        GetForecastInput input,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(new GetForecastOutput(chanceOfRain: 0.4f));
    }
}
