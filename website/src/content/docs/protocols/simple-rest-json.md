---
title: simpleRestJson
description: alloy#simpleRestJson — JSON over HTTP with REST bindings.
---

`alloy#simpleRestJson` is a JSON-over-HTTP protocol with Smithy HTTP bindings.
NSmithy generates both a typed .NET client and an ASP.NET Core minimal API
server adapter. Status: **Preview**.

Use `simpleRestJson` when your consumers are primarily .NET or Scala
(via [Smithy4s](https://disneystreaming.github.io/smithy4s/)) and you want the
smoothest current NSmithy end-to-end path. Use
[`aws.protocols#restJson1`](/smithy-dotnet/protocols/aws-rest-json1/) when you
need broader Smithy ecosystem compatibility or OpenAPI generation.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

```json
"com.disneystreaming.alloy:alloy-core:0.3.38"
```

## NuGet Packages

| Purpose | Package |
| --- | --- |
| Client | `NSmithy.Client` |
| Server (ASP.NET Core) | `NSmithy.Server.AspNetCore` + `Microsoft.AspNetCore.App` |

## Modeling

The example below is adapted from the [Smithy quickstart](https://smithy.io/2.0/quickstart.html).
It demonstrates resources, pagination, errors, and common HTTP binding traits.

```smithy
$version: "2"

namespace example.weather

use alloy#simpleRestJson

/// Provides weather forecasts.
@simpleRestJson
@paginated(inputToken: "nextToken", outputToken: "nextToken", pageSize: "pageSize")
service Weather {
    version: "2006-03-01"
    resources: [City]
    operations: [GetCurrentTime]
}

resource City {
    identifiers: { cityId: CityId }
    properties: { coordinates: CityCoordinates }
    read: GetCity
    list: ListCities
    resources: [Forecast]
}

resource Forecast {
    identifiers: { cityId: CityId }
    properties: { chanceOfRain: Float }
    read: GetForecast
}

@pattern("^[A-Za-z0-9 ]+$")
string CityId

@readonly
@http(method: "GET", uri: "/current-time")
operation GetCurrentTime {
    output := {
        @required
        time: Timestamp
    }
}

@readonly
@http(method: "GET", uri: "/cities/{cityId}")
operation GetCity {
    input := for City {
        @required
        @httpLabel
        $cityId
    }
    output := for City {
        @required
        @notProperty
        name: String

        @required
        $coordinates
    }
    errors: [NoSuchResource]
}

@readonly
@paginated(items: "items")
@http(method: "GET", uri: "/cities")
operation ListCities {
    input := {
        @httpQuery("nextToken")
        nextToken: String

        @httpQuery("pageSize")
        pageSize: Integer
    }
    output := {
        nextToken: String

        @required
        items: CitySummaries
    }
}

@readonly
@http(method: "GET", uri: "/cities/{cityId}/forecast")
operation GetForecast {
    input := for Forecast {
        @required
        @httpLabel
        $cityId
    }
    output := for Forecast {
        $chanceOfRain
    }
}

structure CityCoordinates {
    @required
    latitude: Float

    @required
    longitude: Float
}

list CitySummaries {
    member: CitySummary
}

@references([{resource: City}])
structure CitySummary {
    @required
    cityId: CityId

    @required
    name: String
}

@error("client")
structure NoSuchResource {
    @required
    resourceType: String
}
```

Key HTTP binding traits:

| Trait | Binds member to |
| --- | --- |
| `@httpLabel` | URI path segment |
| `@httpQuery("key")` | query string parameter |
| `@httpHeader("name")` | request or response header |
| `@httpPayload` | raw request/response body |

Members without an explicit binding go into the JSON body.

## Server

NSmithy generates one `IWeatherServiceHandler` interface with a method for each
operation. Implement it once; the generated ASP.NET Core minimal API adapter handles routing,
serialization, and error dispatch.

```csharp
using Example.Weather;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<WeatherHandler>();

var app = builder.Build();
app.MapWeatherServiceHttp();
app.Run();

internal sealed class WeatherHandler : IWeatherServiceHandler
{
    public Task<GetCurrentTimeOutput> GetCurrentTimeAsync(
        GetCurrentTimeInput input, CancellationToken ct = default) =>
        Task.FromResult(new GetCurrentTimeOutput(DateTimeOffset.UtcNow));

    public Task<GetCityOutput> GetCityAsync(
        GetCityInput input, CancellationToken ct = default)
    {
        if (input.CityId == "unknown")
            throw new NoSuchResource(null, "City");

        return Task.FromResult(new GetCityOutput(
            name: "Seattle",
            coordinates: new CityCoordinates(47.6f, -122.3f)
        ));
    }

    public Task<ListCitiesOutput> ListCitiesAsync(
        ListCitiesInput input, CancellationToken ct = default) =>
        Task.FromResult(new ListCitiesOutput(new CitySummaries([
            new CitySummary("SEA", "Seattle"),
            new CitySummary("NYC", "New York"),
        ])));

    public Task<GetForecastOutput> GetForecastAsync(
        GetForecastInput input, CancellationToken ct = default) =>
        Task.FromResult(new GetForecastOutput(chanceOfRain: 0.4f));
}
```

Throwing a generated error type from a handler method causes the adapter to
serialize it with the correct HTTP status code and JSON body.

## Client

```csharp
using Example.Weather;

var client = new WeatherClient(new Uri("http://localhost:5000"));

var time = await client.GetCurrentTimeAsync(new GetCurrentTimeInput());
Console.WriteLine(time.Time);

var cities = await client.ListCitiesAsync(new ListCitiesInput(pageSize: 10));
foreach (var c in cities.Items.Values)
    Console.WriteLine($"{c.CityId}: {c.Name}");

var seattle = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine($"{seattle.Name} ({seattle.Coordinates.Latitude}, {seattle.Coordinates.Longitude})");
```
