using Example.Weather;
using NSmithy.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";

var client = new WeatherClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri(endpoint) }
);

var time = await client.GetCurrentTimeAsync(new GetCurrentTimeInput());
Console.WriteLine($"Current time: {time.Time:u}");

// Paginate through all cities, 3 per page
Console.WriteLine("All cities (paginated, page size 3):");
string? nextToken = null;
var page = 1;
do
{
    var result = await client.ListCitiesAsync(
        new ListCitiesInput(NextToken: nextToken, PageSize: 3)
    );
    Console.WriteLine($"  Page {page++}:");
    foreach (var city in result.Items.Values)
        Console.WriteLine($"    {city.CityId}: {city.Name}");
    nextToken = result.NextToken;
} while (nextToken is not null);

var seattle = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine($"Seattle: ({seattle.Coordinates.Latitude}, {seattle.Coordinates.Longitude})");

var forecast = await client.GetForecastAsync(new GetForecastInput("SEA"));
Console.WriteLine($"Forecast for SEA: {forecast.ChanceOfRain:P0} chance of rain");
