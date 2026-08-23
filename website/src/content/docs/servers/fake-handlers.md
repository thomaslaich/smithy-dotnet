---
title: Fake Handlers
description: Boot a working server from a contract with no handler implementation — canned responses from @examples, placeholders everywhere else.
---

With `SmithyGenerateFakes` enabled, codegen emits a `Fake{Service}Handler`
implementing the full service handler interface. It is an ordinary handler
class — registered through the same `Add{Service}Handler` extension as a real
one — so a bootable server for the whole contract needs no hand-written
handler code:

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
validation still runs in front of the fake, so invalid requests are rejected
with the same `ValidationException` responses a real handler gets.

Projects with an explicit `smithy-build.json` set `"generateFakes": true` on
the `csharp-codegen` plugin instead. The setting requires the server surface
(`SmithyGenerateServer`, on by default).

## Where the responses come from

Responses are compiled into the generated code, so they are deterministic
across calls, runs, and rebuilds of an unchanged model. For each operation:

1. **`@examples` output.** When the operation carries the
   [`@examples` trait](https://smithy.io/2.0/spec/documentation-traits.html#examples-trait),
   the fake returns the output of the first non-error example verbatim.
   Optional members absent from the example are omitted from the response.

2. **Synthesized placeholders.** Without an example, the fake returns values
   synthesized from the shapes: strings echo the member name (`"name"`,
   `"nextToken"`), numbers are `0`, enums and unions take their first variant,
   lists and maps contain a single self-describing entry, and timestamps are a
   fixed instant. `@length` and `@range` minimums are honored;
   `@httpResponseCode` members are `200`. Recursive shapes terminate by
   omitting the recursive optional member or emitting an empty collection.

```smithy
@examples([
    {
        title: "Get Zurich"
        input: { cityId: "zrh" }
        output: { name: "Zurich", coordinates: { latitude: 47.3769, longitude: 8.5417 } }
    }
])
operation GetCity {
    // ...
}
```

```json
// GET /cities/zrh — from the example
{"name":"Zurich","coordinates":{"latitude":47.3769,"longitude":8.5417}}

// GET /cities — no example, synthesized
{"nextToken":"nextToken","items":[{"cityId":"cityId","name":"name"}]}
```

Operations without an example produce a codegen warning naming the operation,
so gaps are visible at build time.

## Replacing fakes one operation at a time

Real handlers can take over individual operations while the fake keeps serving
the rest, in either of two ways.

**Per-operation registration.** `Add{Service}Handler` registers the handler
for the aggregate interface and every per-operation interface, and endpoint
dispatch resolves the per-operation interfaces — the last registration wins.
Register real implementations after the fake:

```csharp
builder.Services.AddWeatherServiceHandler<FakeWeatherServiceHandler>();
builder.Services.AddSingleton<IGetCityHandler, GetCityHandler>();
builder.Services.AddSingleton<IListCitiesHandler, ListCitiesHandler>();
```

Each operation lives in its own class with its own dependencies, so this is
the natural shape for larger projects: implementations land one registration
at a time, independent of each other.

**Subclassing.** The fake's operation methods are `virtual`, so a subclass can
override selected operations in one place and inherit the canned responses for
everything else:

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

This keeps everything in a single registration and makes the fake/real split
visible in one class — a good fit for small services and tests.

Event-stream outputs are served as a short finite stream (the example's
events, or a single synthesized event); streaming blob outputs return an
in-memory stream.
