using Example.Weather;
using NSmithy.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";

var client = new WeatherClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri(endpoint) }
);

var time = await client.GetCurrentTimeAsync(new GetCurrentTimeInput());
Console.WriteLine($"Current time: {time.Time}");

var cities = await client.ListCitiesAsync(new ListCitiesInput(pageSize: 10));
Console.WriteLine($"Cities ({cities.Items.Values.Count}):");
foreach (var c in cities.Items.Values)
    Console.WriteLine($"  {c.CityId}: {c.Name}");

var seattle = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine($"Seattle: ({seattle.Coordinates.Latitude}, {seattle.Coordinates.Longitude})");

var forecast = await client.GetForecastAsync(new GetForecastInput("SEA"));
Console.WriteLine($"Forecast for SEA: {forecast.ChanceOfRain:P0} chance of rain");
