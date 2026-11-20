---
title: F#
description: Generate F# model types, runtime schemas, clients, and servers from a Smithy model.
---

An F# project that references NSmithy generates F#: records and discriminated
unions for the model's shapes, their runtime schemas, and the service's client
and server. Generation runs during `dotnet build`, as it does for C#, and the
generated code uses the same runtime packages.

:::caution[Experimental]
Paginators, waiters, endpoint-rule resolvers, fake clients and handlers, smoke
tests, and client dependency-injection registration are C# only.
:::

The [F# example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/fsharp)
has a server and a client that share a contracts project.

## Set up a project

To start from a template, pass `-lang F#` to `dotnet new nsmithy-contracts`,
`nsmithy-server`, or `nsmithy-client`; see [Quick start](/smithy-dotnet/getting-started/quick-start/).

Name the service with `SmithyService`. NSmithy sees that the project is an F#
project and generates F#. A client project references `NSmithy.Client` and the
package of the service's protocol:

```xml title="Weather.Client.fsproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SmithyService>example.weather#Weather</SmithyService>
    <SmithyGenerateServer>false</SmithyGenerateServer>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="NSmithy.Client" Version="NSMITHY_VERSION" />
    <PackageReference Include="NSmithy.Protocols.RestJson" Version="NSMITHY_VERSION" />
    <PackageReference Include="NSmithy.SmithyCli" Version="1.74.1" />
  </ItemGroup>
</Project>
```

A server project uses the Web SDK and references `NSmithy.Server.AspNetCore`
instead:

```xml title="Weather.Server.fsproj"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SmithyService>example.weather#Weather</SmithyService>
    <SmithyGenerateClient>false</SmithyGenerateClient>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="NSmithy.Server.AspNetCore" Version="NSMITHY_VERSION" />
    <PackageReference Include="NSmithy.Protocols.RestJson" Version="NSMITHY_VERSION" />
    <PackageReference Include="NSmithy.SmithyCli" Version="1.74.1" />
  </ItemGroup>
</Project>
```

Both read the model through a `smithy-build.json`:

```json title="smithy-build.json"
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": ["software.amazon.smithy:smithy-aws-traits:1.74.1"]
  }
}
```

The model can also come from a contracts project, as in
[Distributing contracts](/smithy-dotnet/guides/distributing-contracts/).

Generated files are compiled before the project's own files, so every `.fs`
file in the project can use them.

Like a C# project, an F# project generates the shapes in its own model files and
uses the types that the F# projects it references generated, so the modules of a
server can each own their operations
([Splitting a service across projects](/smithy-dotnet/guides/distributing-contracts/#splitting-a-service-across-projects)).
C# and F# projects do not share generated types.

## What the model becomes

| Model | Generated F# |
| --- | --- |
| Structures | Records, such as `GetCityOutput`. A member that may be absent is an `option`; a member with a default is a plain value that reading fills in. |
| `@error` structures | Exception classes, such as `NoSuchResource`, that take optional members as optional constructor arguments. |
| Unions | Discriminated unions with an `Unknown of tag * value` case for a variant this model does not define. |
| Enums | Discriminated unions with an `Unknown of string` case. `FromValue` and `.Value` convert to and from the wire value. |
| Int enums | F# enums. |
| Lists and maps | `'T list` and `Map<string, 'T>`, with `option` elements when `@sparse`. |
| Operations | A union such as `GetCityError` of the errors each operation declares, which its handler and clients return. |
| The service, when `SmithyGenerateClient` is on | `IWeatherClient`, `WeatherClient`, and `WeatherClientConfig`, plus `WeatherError` for the errors the service declares. |
| The service, when `SmithyGenerateServer` is on | A handler interface `IWeatherServiceHandler` composed of one interface per operation, plus `AddWeatherServiceHandler` and `MapWeatherService` extensions for ASP.NET Core. |
| Every shape | A runtime schema in the child `Schemas` namespace, such as `Example.Weather.Schemas.GetCityOutputSchema`. |

## Serve the service

Implement the handler interface and register it with `AddWeatherServiceHandler`
and `MapWeatherService`; the [Servers](/smithy-dotnet/servers/) pages show each
step in C# and F#. The handler of an operation that declares errors returns a
`Result` with the operation's error union, and the runtime writes the `Error`
case as that error:

```fsharp
member _.GetCityAsync(input, _) =
    match cities.TryFind input.CityId with
    | Some city -> Task.FromResult(Ok city)
    | None -> Task.FromResult(Error(GetCityError.NoSuchResource(NoSuchResource("City"))))
```

Errors the service declares for every operation are not in that union; a
handler raises them, such as `raise (Unavailable())`.

`AddWeatherServiceHandler` also takes a handler instance, such as an object
expression:

```fsharp
builder.Services.AddWeatherServiceHandler
    { new IWeatherServiceHandler with
        member _.GetCityAsync(input, _) = getCity input
        // one member per operation
    }
|> ignore
```

A handler that needs services from the container takes a factory instead. It
runs once by default, or once per request with `ServiceLifetime.Scoped`, which
a handler using a `DbContext` needs:

```fsharp
builder.Services.AddWeatherServiceHandler(
    (fun services -> Weather.create (services.GetRequiredService<WeatherDb>())),
    ServiceLifetime.Scoped
)
|> ignore
```

## Call the service

Each operation is a method that takes the input record and an optional
`CancellationToken`, and returns a `Task`. An operation whose input members are
all optional also has an overload without the input. In a project that
generates only the client, every input member is an `option` (see
[Member optionality](/smithy-dotnet/reference/generated-code/#member-optionality)).
An operation that declares errors returns its output as a `Result` with a union
of those errors:

```fsharp title="Program.fs"
open System
open Example.Weather

let run () =
    task {
        use client = new WeatherClient(Uri "https://weather.example.com")

        match! client.GetCityAsync { CityId = "SEA" } with
        | Ok city -> printfn "%s is at %f, %f" city.Name city.Coordinates.Latitude city.Coordinates.Longitude
        | Error(GetCityError.NoSuchResource error) -> printfn "No such %s" error.ResourceType
    }

(run ()).GetAwaiter().GetResult()
```

Errors the service declares for every operation are one union for the whole
service, `WeatherError`. An operation without errors of its own returns it
directly. One that has both returns a `WeatherCallError`, with the operation's
union in its `Operation` case and the service's in its `Service` case, so one
function handles the service's errors for every call:

```fsharp
let handleServiceError =
    function
    | WeatherError.Unavailable error -> printfn "Try again: %s" error.Message

match! client.GetCityAsync { CityId = "SEA" } with
| Ok city -> printfn "%s" city.Name
| Error(WeatherCallError.Operation(GetCityError.NoSuchResource error)) -> printfn "No such %s" error.ResourceType
| Error(WeatherCallError.Service error) -> handleServiceError error
```

An operation that declares `smithy.framework#ValidationException` has a
`ValidationException` case: the server rejects a request that violates the
model's constraint traits with that error, and a server requires the model to
declare it on every operation whose input is constrained (see
[Validation](/smithy-dotnet/servers/validation/#declaring-the-error)). Declare
it on the operation rather than the service, so that it is a case of the
operation's own union. The case holds the
runtime's `ValidationException`, whose `FieldList` names each member and the
constraint it failed:

```fsharp
match! client.GetCityAsync { CityId = "SEA!" } with
| Ok city -> printfn "%s" city.Name
| Error(GetCityError.NoSuchResource error) -> printfn "No such %s" error.ResourceType
| Error(GetCityError.ValidationException error) ->
    for field in error.FieldList do
        printfn "%s: %s" field.Path field.Message
```

Other failures are exceptions: transport failures, cancellation, and error
responses the model does not declare for the operation.

The client is configured through `WeatherClientConfig` as in C#; see
[Client configuration](/smithy-dotnet/guides/client-configuration/). For code
that uses `async { }`, wrap a call in `Async.AwaitTask`.

## Limitations

- The shapes of two Smithy namespaces cannot refer to each other. F# compiles one
  namespace after another, so generation fails; move the shapes into one
  namespace, for example with the `renameShapes` build transform.
- A service with endpoint rules has no generated endpoint resolver. Set
  `Endpoint` or `EndpointResolver` on the client config.
- The AWS JSON, AWS Query, EC2 Query, and REST XML protocols have no F# client.
