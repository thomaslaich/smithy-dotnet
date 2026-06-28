---
title: AWS restJson1
description: aws.protocols#restJson1 — AWS REST/JSON with Smithy HTTP bindings.
---

`aws.protocols#restJson1` is AWS's REST/JSON protocol. NSmithy generates both a
typed .NET client and an ASP.NET Core minimal API server adapter. Status:
**Preview**.

Although the trait lives under `aws.protocols`, `restJson1` is useful for
non-AWS services too. It has broad Smithy ecosystem support and is the protocol
to choose when you want OpenAPI generation through `smithy-openapi`.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

```json
"software.amazon.smithy:smithy-aws-traits:1.71.0"
```

## NuGet Packages

| Purpose | Package |
| --- | --- |
| Client | `NSmithy.Client` |
| Server (ASP.NET Core) | `NSmithy.Server.AspNetCore` + `Microsoft.AspNetCore.App` |

## Modeling

Use `@restJson1` on the service and Smithy's standard HTTP binding traits on
operations and members:

```smithy
$version: "2"

namespace example.weather

use aws.protocols#restJson1

@restJson1
service Weather {
    version: "2026-01-01"
    operations: [GetCity]
}

@readonly
@http(method: "GET", uri: "/cities/{cityId}")
operation GetCity {
    input := {
        @required
        @httpLabel
        cityId: String
    }
    output := {
        @required
        name: String
    }
    errors: [NoSuchResource]
}

@error("client")
structure NoSuchResource {
    @required
    resourceType: String
}
```

Members without an explicit HTTP binding are serialized in the JSON body.
`restJson1` also carries AWS-specific behavior such as raw string/blob payloads,
`@requestCompression`, `@httpChecksumRequired`, and AWS-style error
deserialization.

## Server

```csharp
using Example.Weather;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<WeatherHandler>();

var app = builder.Build();
app.MapWeatherServiceHttp();
app.Run();

internal sealed class WeatherHandler : IWeatherServiceHandler
{
    public Task<GetCityOutput> GetCityAsync(
        GetCityInput input, CancellationToken ct = default)
    {
        if (input.CityId == "unknown")
            throw new NoSuchResource(null, "City");

        return Task.FromResult(new GetCityOutput("Seattle"));
    }
}
```

## Client

```csharp
using Example.Weather;

var client = new WeatherClient(new Uri("https://api.example.com"));
var city = await client.GetCityAsync(new GetCityInput("SEA"));
Console.WriteLine(city.Name);
```

Explicit SigV4 signing exists in early preview; see
[Authentication](/smithy-dotnet/guides/authentication/). NSmithy does not yet
provide AWS SDK-style endpoint resolution, credential chains, retries, or
pagination helpers. Use the official AWS SDK for .NET for production calls to
AWS services.
