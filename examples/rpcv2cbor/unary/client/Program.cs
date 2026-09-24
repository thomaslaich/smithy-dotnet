using Example.Weather;
using NSmithy.Client;
using NSmithy.Core.Validation;

var endpoint =
    args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? "http://localhost:5001";

var config = new WeatherClientConfig
{
    RetryStrategy = new SmithyStandardRetryStrategy(maxAttempts: 4),
};

// With --debug, log every request and response, including a hex dump of the
// CBOR wire bytes.
if (args.Contains("--debug"))
    config.Interceptors.Add(new DebugInterceptor());

using var client = new WeatherClient(new Uri(endpoint), config);

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

// The flaky endpoint fails two of every three calls with the retryable modeled error; the
// standard retry strategy retries with backoff, so each call below succeeds after
// transparent retries.
Console.WriteLine("Flaky forecasts (each call succeeds after transparent retries):");
for (var i = 0; i < 3; i++)
{
    var flaky = await client.GetFlakyForecastAsync(new GetFlakyForecastInput("SEA"));
    Console.WriteLine($"  Attempt group {i + 1}: {flaky.ChanceOfRain:P0} chance of rain");
}

// Rejected requests come last on purpose: each failed call spends part of the retry strategy's
// shared budget, and the flaky-forecast loop above needs that budget to retry its way to success.

// Modeled errors surface as typed exceptions on the client.
try
{
    await client.GetCityAsync(new GetCityInput("Atlantis"));
}
catch (NoSuchResource error)
{
    Console.WriteLine($"No such {error.ResourceType}: Atlantis");
}

// CityId is @pattern("^[A-Za-z0-9 ]+$"), and constraints are enforced by the server — the
// client sends what it is handed. The rejection comes back as smithy.framework#ValidationException,
// an implicit modeled error on every operation, so it deserializes into a typed exception that
// names the member and the constraint it failed rather than a bare 400.
try
{
    await client.GetCityAsync(new GetCityInput("SEA!"));
}
catch (ValidationException error)
{
    Console.WriteLine($"Rejected \"SEA!\": {error.Message}");
    foreach (var field in error.FieldList)
        Console.WriteLine($"  {field.Path}: {field.Message}");
}
