using Example.Weather;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";

var client = new WeatherClient(new Uri(endpoint));

var time = await client.GetCurrentTimeAsync(new GetCurrentTimeInput());
Console.WriteLine($"Current time: {time.Time:u}");

// Paginate through all cities, 3 per page. The generated pages paginator repeats the call
// while the response carries a continuation token; each page goes through the normal client
// lifecycle (auth, retries, telemetry).
Console.WriteLine("All cities (paginated, page size 3):");
var page = 1;
await foreach (var result in client.ListCitiesPagesAsync(new ListCitiesInput(PageSize: 3)))
{
    Console.WriteLine($"  Page {page++}:");
    foreach (var city in result.Items.Values)
        Console.WriteLine($"    {city.CityId}: {city.Name}");
}

// Or flatten the pages with the items paginator.
var names = new List<string>();
await foreach (var city in client.ListCitiesItemsAsync(new ListCitiesInput(PageSize: 4)))
    names.Add(city.Name);
Console.WriteLine($"All {names.Count} cities, flattened: {string.Join(", ", names)}");

var seattle = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine($"Seattle: ({seattle.Coordinates.Latitude}, {seattle.Coordinates.Longitude})");

var forecast = await client.GetForecastAsync(new GetForecastInput("SEA"));
Console.WriteLine($"Forecast for SEA: {forecast.ChanceOfRain:P0} chance of rain");
