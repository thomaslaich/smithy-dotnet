# NSmithy

[![CI](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/ci.yml)
[![Docs](https://github.com/thomaslaich/smithy-dotnet/actions/workflows/docs.yml/badge.svg?branch=main)](https://thomaslaich.github.io/smithy-dotnet/)
[![NuGet](https://img.shields.io/nuget/v/NSmithy.Client)](https://www.nuget.org/packages/NSmithy.Client)
[![.NET 10](https://img.shields.io/badge/.NET-net10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/thomaslaich/smithy-dotnet)](https://github.com/thomaslaich/smithy-dotnet/blob/main/LICENSE)
[![Smithy CLI](https://img.shields.io/badge/smithy--cli-1.74.1-orange)](https://github.com/smithy-lang/smithy/releases/tag/1.74.1)

**[Docs](https://thomaslaich.github.io/smithy-dotnet/)** · **[Quick start](https://thomaslaich.github.io/smithy-dotnet/getting-started/quick-start/)** · **[Examples](examples/README.md)** · **[Design docs](designs/README.md)** · **[Changelog](CHANGELOG.md)**

NSmithy generates C# and F# clients, ASP.NET Core servers, and model types from [Smithy](https://smithy.io) models as part of `dotnet build`. There is no separate codegen step, and a client and server generated from the same model agree on routes, serialization, and errors.

> **Preview:** expect some API changes before 1.0.

## Why Smithy

Smithy is a protocol-agnostic interface definition language. A model describes a service's operations, data types, and errors once, independent of programming language. AWS defines its entire API surface in Smithy and generates its SDKs from it, and models can be linted, validated, and checked for breaking changes before any code exists. Smithy also has generators for Java, TypeScript, Go, Rust, Python, Kotlin, and Scala, so a .NET service can share one model with clients in those languages.

## Example

A model:

```smithy
@restJson1
service HelloService {
    version: "2006-03-01"
    operations: [SayHello]
}

@readonly
@http(method: "GET", uri: "/hello/{name}")
operation SayHello {
    input := {
        @required
        @httpLabel
        name: String
    }
    output := {
        @required
        message: String
    }
}
```

A server implements the generated handler interface and maps the generated routes:

```csharp
builder.Services.AddHelloServiceHandler<HelloHandler>();
app.MapHelloService();

sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new SayHelloOutput($"Hello, {input.Name}!"));
}
```

A client calls it:

```csharp
var client = new HelloServiceClient(new Uri("http://localhost:5000"));
var response = await client.SayHelloAsync(new SayHelloInput("world"));
```

Add an operation to the model and the server stops compiling until it is handled.

## Quick start

With the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) installed:

```bash
dotnet new install NSmithy.Templates
dotnet new nsmithy-contracts -n HelloWorld.Contracts --smithy-cli-package
dotnet new nsmithy-server -n HelloWorld.Server --contracts-project HelloWorld.Contracts --smithy-cli-package
dotnet run --project HelloWorld.Server
curl http://localhost:5000/hello/world
```

`--smithy-cli-package` gets the Smithy CLI and its Java runtime from NuGet. Omit it to use a [Smithy CLI](https://thomaslaich.github.io/smithy-dotnet/reference/smithy-cli/#installing-the-smithy-cli) on your `PATH`. The [quick start](https://thomaslaich.github.io/smithy-dotnet/getting-started/quick-start/) adds a client and walks through the generated code.

## Features

- **Protocols:** `restJson1`, `simpleRestJson`, `rpcv2Cbor`, `rpcv2Json`, and gRPC for clients and servers; `awsJson1_0`, `awsJson1_1`, `awsQuery`, `ec2Query`, and `restXml` for clients. Coverage against the official Smithy protocol tests is on the [protocol status](https://thomaslaich.github.io/smithy-dotnet/protocols/status/) page.
- **Clients:** [retries](https://thomaslaich.github.io/smithy-dotnet/guides/client-configuration/retry/), [pagination](https://thomaslaich.github.io/smithy-dotnet/guides/client-configuration/pagination/), [waiters](https://thomaslaich.github.io/smithy-dotnet/guides/client-configuration/waiters/), [endpoint rules](https://thomaslaich.github.io/smithy-dotnet/guides/client-configuration/endpoint-rules/), [interceptors](https://thomaslaich.github.io/smithy-dotnet/guides/client-configuration/interceptors/), and [fake clients](https://thomaslaich.github.io/smithy-dotnet/guides/client-configuration/fake-clients/) for tests.
- **Servers:** ASP.NET Core minimal APIs with [validation](https://thomaslaich.github.io/smithy-dotnet/servers/validation/), [modeled authorization](https://thomaslaich.github.io/smithy-dotnet/servers/authorization/), [fake handlers](https://thomaslaich.github.io/smithy-dotnet/servers/fake-handlers/) built from `@examples`, generated [API docs](https://thomaslaich.github.io/smithy-dotnet/guides/endpoint-documentation/), and an experimental [MCP adapter](https://thomaslaich.github.io/smithy-dotnet/servers/mcp/).
- **F#:** records, discriminated unions, clients, and servers. Paginators, waiters, endpoint rules, and fakes are C# only. See the [F# guide](https://thomaslaich.github.io/smithy-dotnet/guides/fsharp/).
- **Distribution:** share a model as a NuGet package or a Maven JAR. See [distributing contracts](https://thomaslaich.github.io/smithy-dotnet/guides/distributing-contracts/).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development environment and build recipes.

## Related projects

- [Smithy](https://smithy.io), the IDL NSmithy is built on.
- [smithy4s](https://disneystreaming.github.io/smithy4s/), the main inspiration for NSmithy.
- [alloy](https://github.com/disneystreaming/alloy), used for `simpleRestJson` and gRPC.
- [smithy-go](https://github.com/smithy-lang/smithy-go) and [smithy-typescript](https://github.com/smithy-lang/smithy-typescript), the official generators for Go and TypeScript.
- [TypeSpec](https://typespec.io), Microsoft's alternative with first-party .NET emitters.
