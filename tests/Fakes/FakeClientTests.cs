using Example.Weather;

namespace NSmithy.Fakes.Tests;

/// <summary>
/// Stand-in for application code under test. It depends only on the generated
/// <see cref="IWeatherClient"/> interface and never sees the fake.
/// </summary>
internal static class CityReport
{
    /// <summary>Names of all cities north of the 45th parallel.</summary>
    public static async Task<IReadOnlyList<string>> NorthernCityNamesAsync(IWeatherClient client)
    {
        var names = new List<string>();
        await foreach (var summary in client.ListCitiesItemsAsync(new ListCitiesInput()))
        {
            var city = await client.GetCityAsync(new GetCityInput(summary.CityId));
            if (city.Coordinates.Latitude > 45)
                names.Add(city.Name);
        }
        return names;
    }
}

public class FakeClientTests
{
    [Fact]
    public async Task ConsumerLogicRunsAgainstTheFakeWithoutAServer()
    {
        // Out of the box: ListCities has no @examples, so the fake returns one
        // synthesized summary; GetCity returns its @examples entry (Seattle).
        using IWeatherClient client = new FakeWeatherClient();

        var names = await CityReport.NorthernCityNamesAsync(client);

        Assert.Equal(["Seattle"], names);
    }

    [Fact]
    public async Task OverridingOneOperationPinsTheDataATestAssertsOn()
    {
        using IWeatherClient client = new TwoCitiesWeatherClient();

        var names = await CityReport.NorthernCityNamesAsync(client);

        Assert.Equal(["Seattle"], names);
    }

    [Fact]
    public async Task FakePaginatorsYieldASinglePage()
    {
        // The synthesized ListCities output carries a non-null nextToken; the
        // fake paginator still terminates after one page.
        using var client = new FakeWeatherClient();

        var pages = 0;
        await foreach (var _ in client.ListCitiesPagesAsync(new ListCitiesInput()))
            pages++;

        Assert.Equal(1, pages);
    }

    [Fact]
    public async Task PlaceholderValuesAreDeterministic()
    {
        using var client = new FakeWeatherClient();

        var time = await client.GetCurrentTimeAsync(new GetCurrentTimeInput());

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1704067200), time.Time);
    }

    /// <summary>
    /// Overrides only the operations whose data the test asserts on; every
    /// other operation keeps its canned response.
    /// </summary>
    private sealed class TwoCitiesWeatherClient : FakeWeatherClient
    {
        public override Task<ListCitiesOutput> ListCitiesAsync(
            ListCitiesInput input,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ListCitiesOutput(
                    Items: new CitySummaries(
                        new[]
                        {
                            new CitySummary(CityId: "SEA", Name: "Seattle"),
                            new CitySummary(CityId: "HOU", Name: "Houston"),
                        }
                    )
                )
            );

        public override Task<GetCityOutput> GetCityAsync(
            GetCityInput input,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                input.CityId == "SEA"
                    ? new GetCityOutput(
                        Name: "Seattle",
                        Coordinates: new CityCoordinates(Latitude: 47.6f, Longitude: -122.3f)
                    )
                    : new GetCityOutput(
                        Name: "Houston",
                        Coordinates: new CityCoordinates(Latitude: 29.8f, Longitude: -95.4f)
                    )
            );
    }
}
