---
title: Fake Handlers
description: Boot a working server from a contract with no handler implementation. Canned responses from @examples, placeholders everywhere else.
---

With `SmithyGenerateFakes` enabled, codegen emits a `Fake{Service}Handler`
implementing the full service handler interface. A working server without
writing a handler:

```xml
<PropertyGroup>
  <SmithyGenerateFakes>true</SmithyGenerateFakes>
</PropertyGroup>
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<FakeWeatherServiceHandler>();

var app = builder.Build();
app.MapWeatherService();
app.Run();
```

Every operation responds on every protocol the service declares. Input
validation still runs in front of the fake: invalid requests are rejected
with the same `ValidationException` responses a real handler gets.

Projects with an explicit `smithy-build.json` set `"generateFakes": true` on
the `csharp-codegen` plugin instead. The fake handler is generated with the
server surface (`SmithyGenerateServer`, on by default); the same setting also
generates a [fake client](/smithy-dotnet/guides/client-configuration/fake-clients/)
with the client surface.

## Where the responses come from

Responses are compiled into the generated code, so they are deterministic
across calls, runs, and rebuilds of an unchanged model. For each operation:

1. **`@examples` input matching.** When the operation carries several
   [`@examples`](https://smithy.io/2.0/spec/documentation-traits.html#examples-trait)
   entries, the incoming input is matched against the example inputs in model
   order and the first match decides the response. Matching is a subset
   comparison: members present in the example input must equal the incoming
   input, members absent from the example match anything, at every nesting
   level. A matched error example throws the modeled error, which the server
   serializes like any handler-thrown error.

2. **`@examples` output.** When no example input matches (or the operation
   has a single example), the fake returns the output of the first non-error
   example. Optional members absent from the example are omitted.

3. **Synthesized placeholders.** Without an example, strings echo the member
   name (`"name"`, `"nextToken"`), numbers are `0`, enums and unions take
   their first variant, lists and maps contain a single entry, and timestamps
   are a fixed instant. `@length` and `@range` minimums are honored;
   `@httpResponseCode` members are `200`. Recursive shapes terminate by
   omitting the recursive optional member or emitting an empty collection.

```smithy
@examples([
    {
        title: "Get Zurich"
        input: { cityId: "zrh" }
        output: { name: "Zurich", coordinates: { latitude: 47.3769, longitude: 8.5417 } }
    }
    {
        title: "Get Geneva"
        input: { cityId: "gva" }
        output: { name: "Geneva", coordinates: { latitude: 46.2044, longitude: 6.1432 } }
    }
    {
        title: "Get unknown city"
        input: { cityId: "xxx" }
        error: {
            shapeId: "example.weather#NoSuchCity"
            content: { message: "no city with ID xxx" }
        }
    }
])
operation GetCity {
    // ...
}
```

```json
// GET /cities/zrh: from the matched example
{"name":"Zurich","coordinates":{"latitude":47.3769,"longitude":8.5417}}

// GET /cities/gva: from the matched example
{"name":"Geneva","coordinates":{"latitude":46.2044,"longitude":6.1432}}

// GET /cities/xxx: the matched error example, served as a modeled error
// (404, X-Amzn-Errortype: NoSuchCity)
{"message":"no city with ID xxx"}

// GET /cities/brn: no match, first non-error example
{"name":"Zurich","coordinates":{"latitude":47.3769,"longitude":8.5417}}

// GET /cities: no example, synthesized
{"nextToken":"nextToken","items":[{"cityId":"cityId","name":"name"}]}
```

Operations without an example produce a codegen warning naming the operation.

## Replacing fakes one operation at a time

**Per-operation registration.** `Add{Service}Handler` registers the handler
under every per-operation interface, and the last registration wins:

```csharp
builder.Services.AddWeatherServiceHandler<FakeWeatherServiceHandler>();
builder.Services.AddSingleton<IGetCityHandler, GetCityHandler>();
builder.Services.AddSingleton<IListCitiesHandler, ListCitiesHandler>();
```

**Subclassing.** The fake's operation methods are `virtual`:

```csharp
internal sealed class WeatherHandler : FakeWeatherServiceHandler
{
    public override Task<GetCityOutput> GetCityAsync(
        GetCityInput input,
        CancellationToken cancellationToken = default
    ) => /* real implementation */;
}

builder.Services.AddWeatherServiceHandler<WeatherHandler>();
```

Event-stream outputs are served as a short finite stream (the example's
events, or a single synthesized event); streaming blob outputs return an
in-memory stream.
