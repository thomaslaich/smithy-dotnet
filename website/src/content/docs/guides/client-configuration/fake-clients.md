---
title: Fake Clients
description: A generated client that returns canned responses without a network call. Canned responses from @examples, placeholders everywhere else.
---

With `SmithyGenerateFakes` enabled, codegen emits a `Fake{Service}Client`
implementing `I{Service}Client`. Code that depends on the client interface
runs against it with no server and no network:

```xml
<PropertyGroup>
  <SmithyGenerateFakes>true</SmithyGenerateFakes>
</PropertyGroup>
```

```csharp
IWeatherClient client = new FakeWeatherClient();
var city = await client.GetCityAsync(new GetCityInput(CityId: "zrh"));
```

With the service container, register it in place of the real client:

```csharp
services.AddSingleton<IWeatherClient, FakeWeatherClient>();
```

Responses come from the same synthesis as
[fake handlers](/smithy-dotnet/servers/fake-handlers/#where-the-responses-come-from):
the output of each operation's first non-error `@examples` entry when
present, deterministic placeholders otherwise. The fake client is generated
with the client surface (`SmithyGenerateClient`, on by default). Projects
with an explicit `smithy-build.json` set `"generateFakes": true` on the
`csharp-codegen` plugin instead.

## Differences from the real client

- No serialization, protocol, or validation is involved. Every call returns
  the canned response, including calls a real client or server would reject.
- Paginators yield a single page. The canned output's continuation token may
  be non-null, and following it would never terminate.
- Every operation responds, including event-stream operations the real
  client rejects when no declared protocol supports them.

## Replacing fakes one operation at a time

The fake's operation methods are `virtual`. In a test, override the operations
whose data the test asserts on; everything else keeps its canned response:

```csharp
internal sealed class TwoCitiesWeatherClient : FakeWeatherClient
{
    public override Task<ListCitiesOutput> ListCitiesAsync(
        ListCitiesInput input,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            new ListCitiesOutput(
                Items: new CitySummaries(
                    [
                        new CitySummary(CityId: "SEA", Name: "Seattle"),
                        new CitySummary(CityId: "HOU", Name: "Houston"),
                    ]
                )
            )
        );
}
```

Paginators call the virtual unary method, so overriding it also changes the
page the paginators yield.
