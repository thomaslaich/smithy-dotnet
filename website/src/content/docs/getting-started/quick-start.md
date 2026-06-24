---
title: Quick Start
description: Scaffold a contracts project, a server, and a client with NSmithy templates.
---

If you are new to Smithy, the [official Smithy quickstart](https://smithy.io/2.0/quickstart.html)
is a good place to learn the IDL before continuing.

## Prerequisites

You need the [.NET SDK](https://dotnet.microsoft.com/download). That's it for
basic builds — NSmithy bundles the Smithy CLI (including a JRE) inside the
NuGet package, so no separate Java or Smithy CLI installation is required.

If you enable `SmithyGenerateDocs` (Sphinx HTML docs), Python 3.11+ must also
be on your PATH.

Optionally install [CSharpier](https://csharpier.com) for formatted generated
code. NSmithy runs it automatically after codegen when it is available, and skips
it otherwise.

Install the NSmithy project templates (one-time):

```shell
dotnet new install NSmithy.Templates
```

## 1. Create the Contracts Project

The contracts project owns the Smithy model and distributes it to the server
and client.

```shell
mkdir HelloWorld && cd HelloWorld
dotnet new sln -n HelloWorld
dotnet new nsmithy-contracts -n HelloWorld.Contracts
dotnet sln add HelloWorld.Contracts
```

This generates:

```
HelloWorld.Contracts/
  HelloWorld.Contracts.csproj
  model/
    service.smithy          ← starter restJson1 HelloService model
```

## 2. Create the Server

```shell
dotnet new nsmithy-server -n HelloWorld.Server --contracts HelloWorld.Contracts --with-docs
dotnet sln add HelloWorld.Server
```

The `--contracts` flag adds a `ProjectReference` to the contracts project.
`--with-docs` enables the Smithy docs and OpenAPI endpoints. Build and run:

```shell
dotnet run --project HelloWorld.Server
```

The server listens on `http://localhost:5000`. Test it:

```shell
curl http://localhost:5000/hello/world
# {"message":"Hello, world!"}
```

With `--with-docs`, two documentation UIs are also available:

- **`/docs`** — Smithy-generated reference docs for your model
- **`/openapi`** — interactive Scalar UI backed by a generated `openapi.json`

See [Endpoint Documentation](/smithy-dotnet/guides/endpoint-documentation/) for details.

## 3. Create the Client

```shell
dotnet new nsmithy-client -n HelloWorld.Client
dotnet sln add HelloWorld.Client
```

The client template defaults to a Maven contracts reference for production use.
For local development, open `HelloWorld.Client/HelloWorld.Client.csproj` and
replace the `SmithyMavenDependency` placeholder with a `ProjectReference`:

```xml
<!-- remove this -->
<SmithyMavenDependency Include="io.github.YOUR_ORG:your-service-contracts:1.0.0" />

<!-- add this instead -->
<ProjectReference Include="../HelloWorld.Contracts/HelloWorld.Contracts.csproj" />
```

Then run the client with the server still running:

```shell
dotnet run --project HelloWorld.Client
# Hello, world!
```

When you're ready to distribute, see
[Distributing Contracts](/smithy-dotnet/guides/distributing-contracts/) to
publish the contracts JAR and switch back to a Maven reference.

## Walking Through the Code

### The Model

Open `HelloWorld.Contracts/model/service.smithy`. The template generates a
minimal `restJson1` service with a single operation:

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

`@restJson1` is the protocol; it controls serialization and HTTP binding
behavior. `@http` binds the operation to a route. `@httpLabel` maps `name` to
the `{name}` path segment. This is the source of truth for everything that
follows — change the model, rebuild, and all generated code updates automatically.

### Generated Types

Running `dotnet build` invokes the Smithy CLI and generates C# types under
`obj/`. For the model above you get:

```csharp
public sealed record SayHelloInput(string Name);
public sealed record SayHelloOutput(string Message);
```

And a handler interface the server must implement:

```csharp
public interface IHelloServiceHandler
{
    Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken ct = default);
}
```

### The Server Handler

The generated server uses [ASP.NET Core minimal API](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis).
`Program.cs` registers your handler and maps the routes:

```csharp
builder.Services.AddHelloServiceHandler<HelloHandler>();

app.MapHelloServiceHttp();
```

`HelloHandler` (also generated as a starter) simply returns a greeting:

```csharp
internal sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new SayHelloOutput($"Hello, {input.Name}!"));
}
```

Replace the body with your real logic. The compiler enforces that every
operation in the model has an implementation — add an operation to the model and
the build breaks until you handle it.

### The Generated Client

`HelloWorld.Client/Program.cs` uses the generated typed client:

```csharp
var client = new HelloServiceClient(new Uri(endpoint));

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message);
```

The client and server share the same generated input/output types from the
contracts project. There is no hand-written glue — the model is the contract.

## Template Options

All three templates accept `--protocol`:

| Value | Protocol |
| --- | --- |
| `restJson1` | `aws.protocols#restJson1` (default) |
| `simpleRestJson` | `alloy#simpleRestJson` |
| `grpc` | `alloy.proto#grpc` (experimental) |

Additional options:

```shell
dotnet new nsmithy-server --help
dotnet new nsmithy-contracts --help
dotnet new nsmithy-client --help
```

## Example Repository

For a more complete example with multiple operations, error types, and a working
client/server setup, see
[nsmithy-minimal](https://github.com/thomaslaich/nsmithy-minimal).
