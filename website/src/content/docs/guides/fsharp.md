---
title: F#
description: Generate F# model types, runtime schemas, and clients from a Smithy model.
---

An F# project that references NSmithy generates F#: records and discriminated
unions for the model's shapes, their runtime schemas, and the service's client.
Generation runs during `dotnet build`, as it does for C#, and the generated
code uses the same runtime packages.

:::caution[Experimental]
F# generates model types and clients. Servers, paginators, waiters, endpoint-rule
resolvers, fake clients, and dependency-injection registration are C# only.
:::

## Set up a project

Reference `NSmithy.Client` and the package of the service's protocol, and name
the service with `SmithyService`. NSmithy sees that the project is an F# project
and generates F#.

```xml title="Weather.Client.fsproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SmithyService>example.weather#Weather</SmithyService>
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

## What the model becomes

| Model | Generated F# |
| --- | --- |
| Structures | Records, such as `GetCityOutput`. A member that may be absent is an `option`; a member with a default is a plain value that reading fills in. |
| `@error` structures | Exception classes, such as `NoSuchResource`, that take optional members as optional constructor arguments. |
| Unions | Discriminated unions with an `Unknown of tag * value` case for a variant this model does not define. |
| Enums | Discriminated unions with an `Unknown of string` case. `FromValue` and `.Value` convert to and from the wire value. |
| Int enums | F# enums. |
| Lists and maps | `'T list` and `Map<string, 'T>`, with `option` elements when `@sparse`. |
| The service, when `SmithyGenerateClient` is on | `IWeatherClient`, `WeatherClient`, and `WeatherClientConfig`. |
| Every shape | A runtime schema in the child `Schemas` namespace, such as `Example.Weather.Schemas.GetCityOutputSchema`. |

## Call the service

Each operation is a method that takes the input record and an optional
`CancellationToken`, and returns a `Task`:

```fsharp title="Program.fs"
open System
open Example.Weather

let run () =
    task {
        use client = new WeatherClient(Uri "https://weather.example.com")

        try
            let! city = client.GetCityAsync { CityId = "SEA" }
            printfn "%s is at %f, %f" city.Name city.Coordinates.Latitude city.Coordinates.Longitude
        with :? NoSuchResource as error ->
            printfn "No such %s" error.ResourceType
    }

run().Wait()
```

The client runs on the same runtime as the C# client, so
[authentication](/smithy-dotnet/guides/client-configuration/authentication/),
[retries](/smithy-dotnet/guides/client-configuration/retry/),
[interceptors](/smithy-dotnet/guides/client-configuration/interceptors/), and
[transport](/smithy-dotnet/guides/client-configuration/transport/) are set on
`WeatherClientConfig` the same way. For code that uses `async { }`, wrap a call
in `Async.AwaitTask`.

## Limitations

- The shapes of two Smithy namespaces cannot refer to each other. F# compiles one
  namespace after another, so generation fails; move the shapes into one
  namespace, for example with the `renameShapes` build transform.
- A service with endpoint rules has no generated endpoint resolver. Set
  `Endpoint` or `EndpointResolver` on the client config.
- The AWS JSON, AWS Query, EC2 Query, and REST XML protocols have no F# client.
