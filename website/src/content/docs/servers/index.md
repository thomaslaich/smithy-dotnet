---
title: Servers
description: Implement one handler interface; the generated ASP.NET Core adapter and the shared server runtime handle routing, serialization, and error dispatch.
---

NSmithy generates one handler interface per service — a method per operation,
in plain model types. You implement it once. The generated ASP.NET Core adapter
converts each request, the shared server runtime dispatches it (deserialize →
invoke → serialize, or serialize a modeled error), and the response goes back on
the wire. None of that request-handling machinery depends on which protocol the
service declares.

The service handler interface (`IWeatherServiceHandler`) is composed from one
**per-operation interface** for each operation — `IGetCityHandler`,
`IListCitiesHandler`, and so on — which it inherits:

```csharp
public interface IGetCityHandler
{
    Task<GetCityOutput> GetCityAsync(GetCityInput input, CancellationToken ct = default);
}

public interface IWeatherServiceHandler : IGetCityHandler, IListCitiesHandler { }
```

Registration wires up both the aggregate interface and each per-operation
interface to your single implementation, so a component can depend on just the
one operation it needs (`IGetCityHandler`) instead of the whole service. The
model and the handler are the same across every protocol — see the [Protocols
Overview](/smithy-dotnet/protocols/overview/) for the shared model.

## Register the handler

`Add{Service}Handler<THandler>` registers one implementation of the whole
service against both the aggregate interface and each per-operation interface in
DI:

```csharp
using Example.Weather;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<WeatherHandler>();
```

### Registering operation handlers separately

The generated endpoints resolve the **per-operation interface** from DI — never
the aggregate — so a single class implementing the whole service is a
convenience, not a requirement. If you'd rather split operations across classes
(or across teams), register each per-operation interface yourself and skip
`AddWeatherServiceHandler`:

```csharp
builder.Services.AddSingleton<IGetCityHandler, GetCityHandler>();
builder.Services.AddSingleton<IListCitiesHandler, ListCitiesHandler>();
// …one registration per operation the mapped protocol serves
```

Each mapped route picks up its operation's handler independently. As long as
every operation the protocol maps has a registration, the service is fully
served.

## Map the endpoints

Each declared protocol generates a `Map{Service}{Protocol}` extension —
`MapWeatherServiceRestJson1`, `MapWeatherServiceRpcV2Cbor`,
`MapWeatherServiceGrpc`, and so on. Call the one for the protocol your service
declares:

```csharp
var app = builder.Build();
app.MapWeatherServiceRestJson1();
app.Run();
```

The endpoint is thin: it binds the route to your handler method and the
operation's bound protocol, and delegates to the runtime. A service that
declares several protocols gets one `Map` per protocol and can serve them all
from the same handler — see [Hosting &amp; Multiple Protocols](/smithy-dotnet/servers/hosting/).

## Implement the handler

Return the modeled output; throw a generated error type to return a modeled
error. The protocol serializes each with the correct status code and body:

```csharp
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

Error identity and status come from the model, so the same thrown exception
serializes correctly for every protocol the service exposes.

A handler that implements a single per-operation interface looks the same, minus
the other operations — implement just the one method:

```csharp
internal sealed class GetCityHandler : IGetCityHandler
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

Register it with `AddSingleton<IGetCityHandler, GetCityHandler>()` (see
[Registering operation handlers separately](#registering-operation-handlers-separately)).
The endpoint that maps `GetCity` resolves this handler directly, so it serves that
operation whether or not the rest of the service is implemented by the same class.

## Which protocols generate a server

Server generation exists for `simpleRestJson`, `restJson1`, `rpcv2Cbor`, and
`gRPC`. `awsJson1_1`/`awsJson1_0` and `restXml` are **client-only** today. See
[Protocol Status](/smithy-dotnet/protocols/status/) for the current matrix.

gRPC needs HTTP/2 transport and its own listener — see
[gRPC](/smithy-dotnet/protocols/grpc/) for the Kestrel setup.
