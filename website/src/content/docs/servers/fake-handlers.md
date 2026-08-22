---
title: Fake Handlers
description: Boot a working server from a contract with no handler implementation — canned responses from @examples, placeholders everywhere else.
---

With `SmithyGenerateFakes` enabled, codegen emits a `Fake{Service}Handler`
implementing the full service handler interface, plus a registration
extension. Registering it gives a bootable server for the whole contract with
no hand-written handler code:

```xml
<PropertyGroup>
  <SmithyGenerateFakes>true</SmithyGenerateFakes>
</PropertyGroup>
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFakeWeatherServiceHandler();

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

`AddFake{Service}Handler` registers the fake for the aggregate interface and
every per-operation interface. Endpoint dispatch resolves the per-operation
interfaces, so a real handler can take over individual operations while the
fake keeps serving the rest — register the real implementation after the fake:

```csharp
builder.Services.AddFakeWeatherServiceHandler();
builder.Services.AddSingleton<IGetCityHandler, GetCityHandler>();
```

Event-stream outputs are served as a short finite stream (the example's
events, or a single synthesized event); streaming blob outputs return an
in-memory stream.
