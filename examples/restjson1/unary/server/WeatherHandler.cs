using Example.Weather;

internal sealed class WeatherHandler : IWeatherServiceHandler
{
    private static readonly List<(
        string CityId,
        string Name,
        float Latitude,
        float Longitude
    )> Cities =
    [
        ("SEA", "Seattle", 47.6f, -122.3f),
        ("NYC", "New York", 40.7f, -74.0f),
        ("LAX", "Los Angeles", 34.1f, -118.2f),
        ("CHI", "Chicago", 41.9f, -87.6f),
        ("HOU", "Houston", 29.8f, -95.4f),
        ("PHX", "Phoenix", 33.4f, -112.1f),
        ("PHL", "Philadelphia", 39.9f, -75.2f),
        ("SAN", "San Antonio", 29.4f, -98.5f),
        ("SDA", "San Diego", 32.7f, -117.2f),
        ("DAL", "Dallas", 32.8f, -96.8f),
    ];

    private int flakyCalls;

    public Task<GetCurrentTimeOutput> GetCurrentTimeAsync(
        GetCurrentTimeInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new GetCurrentTimeOutput(DateTimeOffset.UtcNow));

    public Task<GetCityOutput> GetCityAsync(
        GetCityInput input,
        CancellationToken cancellationToken = default
    )
    {
        var city = Cities.FirstOrDefault(c => c.CityId == input.CityId);
        if (city == default)
            throw new NoSuchResource(null, "City");

        return Task.FromResult(
            new GetCityOutput(
                Coordinates: new CityCoordinates(city.Latitude, city.Longitude),
                Name: city.Name
            )
        );
    }

    public Task<ListCitiesOutput> ListCitiesAsync(
        ListCitiesInput input,
        CancellationToken cancellationToken = default
    )
    {
        var pageSize = input.PageSize ?? 3;
        var startIndex = 0;

        if (input.NextToken is { } token)
        {
            var idx = Cities.FindIndex(c => c.CityId == token);
            startIndex = idx < 0 ? 0 : idx + 1;
        }

        var page = Cities.Skip(startIndex).Take(pageSize).ToList();
        var nextToken = startIndex + page.Count < Cities.Count ? page.Last().CityId : null;

        return Task.FromResult(
            new ListCitiesOutput(
                Items: new CitySummaries([.. page.Select(c => new CitySummary(c.CityId, c.Name))]),
                NextToken: nextToken
            )
        );
    }

    public Task<GetForecastOutput> GetForecastAsync(
        GetForecastInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new GetForecastOutput(ChanceOfRain: 0.4f));

    public Task<GetFlakyForecastOutput> GetFlakyForecastAsync(
        GetFlakyForecastInput input,
        CancellationToken cancellationToken = default
    )
    {
        // Fail two of every three calls with the retryable modeled error, so a client with a
        // retry strategy succeeds after visible retries (see the attempt spans in Grafana).
        if (Interlocked.Increment(ref flakyCalls) % 3 != 0)
            throw new ServiceUnavailable("The forecast backend is overloaded; try again.");

        return Task.FromResult(new GetFlakyForecastOutput(ChanceOfRain: 0.4f));
    }
}
