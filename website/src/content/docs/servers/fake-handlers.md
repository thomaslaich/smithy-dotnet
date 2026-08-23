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
validation runs in front of the fake.

Projects with an explicit `smithy-build.json` set `"generateFakes": true` on
the `csharp-codegen` plugin instead. The setting requires the server surface
(`SmithyGenerateServer`, on by default).

## Where the responses come from

Responses are compiled into the generated code and are deterministic. For
each operation:

1. **`@examples` output.** When the operation carries the
   [`@examples` trait](https://smithy.io/2.0/spec/documentation-traits.html#examples-trait),
   the fake returns the output of the first non-error example. Optional
   members absent from the example are omitted.

2. **Synthesized placeholders.** Without an example, strings echo the member
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
])
operation GetCity {
    // ...
}
```

```json
// GET /cities/zrh: from the example
{"name":"Zurich","coordinates":{"latitude":47.3769,"longitude":8.5417}}

// GET /cities: no example, synthesized
{"nextToken":"nextToken","items":[{"cityId":"cityId","name":"name"}]}
```

Operations without an example produce a codegen warning.

## Replacing fakes one operation at a time

**Per-operation registration** is the way to go for larger projects.
`Add{Service}Handler` registers the handler under every per-operation
interface, and the last registration wins:

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

Event-stream outputs are served as a short finite stream; streaming blob
outputs return an in-memory stream.
