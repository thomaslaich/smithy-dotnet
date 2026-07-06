using Example.Weather;
using NSmithy.Client;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";

// Export client telemetry over OTLP (defaults to http://localhost:4317, where
// grafana/otel-lgtm listens). The NSmithy client runtime emits one span per
// operation with a child span per transport attempt, plus attempt/error/duration
// metrics — subscribing to "NSmithy.Client" is all it takes.
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .ConfigureResource(resource => resource.AddService("weather-client"))
    .AddSource(SmithyClientTelemetry.ActivitySourceName)
    .AddOtlpExporter()
    .Build();
using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .ConfigureResource(resource => resource.AddService("weather-client"))
    .AddMeter(SmithyClientTelemetry.MeterName)
    .AddOtlpExporter()
    .Build();

using var client = new WeatherClient(
    new Uri(endpoint),
    new() { RetryStrategy = new SmithyStandardRetryStrategy(maxAttempts: 4) }
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

// The flaky endpoint fails two of every three calls with a retryable 503; the standard retry
// strategy retries with backoff, so each call below succeeds after visible retries. In Grafana
// (Tempo), each Weather.GetFlakyForecast trace shows the failed attempt spans that preceded the
// successful one.
Console.WriteLine("Flaky forecasts (each call succeeds after transparent retries):");
for (var i = 0; i < 3; i++)
{
    var flaky = await client.GetFlakyForecastAsync(new GetFlakyForecastInput("SEA"));
    Console.WriteLine($"  Attempt group {i + 1}: {flaky.ChanceOfRain:P0} chance of rain");
}
