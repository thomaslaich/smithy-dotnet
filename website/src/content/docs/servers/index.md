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

Each server generates a `Map{Service}` extension. By default it maps the first
declared server protocol; for multi-protocol services, pass the generated
`{Service}Protocols` flags enum to select one or more protocols:

```csharp
var app = builder.Build();
app.MapWeatherService();
app.Run();
```

The endpoint is thin: it binds the route to your handler method and the
operation's bound protocol, and delegates to the runtime. A service that
declares several protocols can serve selected protocols from the same handler —
see [Hosting &amp; Multiple Protocols](/smithy-dotnet/servers/hosting/).

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

## Constraint validation

The server checks the model's constraint traits — `@required`, `@length`,
`@range`, `@pattern`, `@uniqueItems` — plus enum membership, after deserializing
the request and before calling the handler. A handler only sees input the model
permits, so it does not need to re-check what the model already states.

Enum types stay open on the wire so that a client is not broken by a server that
adds a member; the server is where that openness stops.

A request that violates a constraint gets `smithy.framework#ValidationException`
with HTTP 400, listing each violation with a JSONPointer path into the input:

```json
{
  "message": "2 validation errors detected.",
  "fieldList": [
    { "path": "/name", "message": "Value at '/name' length 1 is less than minimum 3." },
    { "path": "/tags/0", "message": "Value at '/tags/0' does not match pattern '^[a-z]+$'." }
  ]
}
```

Every operation carries this error implicitly, so a generated client deserializes
it as a modeled `ValidationException` whether or not the model declares it.

Validation is compiled from the schema once, when the service starts. An
operation whose input carries no constraints skips validation entirely.

Clients deliberately do not validate outbound input. The server is the authority
on the contract, so checking on the client would duplicate the check that
actually decides, add latency to every call, and go stale as soon as the model
changes without a client rebuild. A generated client sends what it is given and
surfaces the server's `ValidationException` as a modeled error.

## Which protocols generate a server

Server generation exists for `simpleRestJson`, `restJson1`, `rpcv2Cbor`, and
`gRPC`. `awsJson1_1`/`awsJson1_0` and `restXml` are **client-only** today. See
[Protocol Status](/smithy-dotnet/protocols/status/) for the current matrix.

gRPC needs HTTP/2 transport and its own listener — see
[gRPC](/smithy-dotnet/protocols/grpc/) for the Kestrel setup.
