---
title: Quick start
description: Scaffold a contracts project, a server, and a client with NSmithy templates.
---

If you are new to Smithy, the [official Smithy quickstart](https://smithy.io/2.0/quickstart.html)
is a good place to learn the IDL before continuing.

## Prerequisites

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
NSmithy bundles the Smithy CLI (including a JRE) inside the NuGet package, so no
separate Java or Smithy CLI installation is required.

The server command below uses `--with-docs` to generate Sphinx HTML documentation.
This requires Python 3.11+ on your PATH. Omit `--with-docs` if you only want to
run the service.

Install the NSmithy project templates (one-time):

```shell
dotnet new install NSmithy.Templates
```

## 1. Create the contracts project

The contracts project owns the Smithy model. The server and client reference
it and generate their own C# types during the build.

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

## 2. Create the server

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

## 3. Create the client

In another terminal, from the solution directory:

```shell
dotnet new nsmithy-client -n HelloWorld.Client
dotnet sln add HelloWorld.Client
```

The template includes a `smithy-build.json` with a placeholder Maven dependency.
For this example, use the sibling contracts project instead. Delete
the client's `smithy-build.json` — when no `smithy-build.json` exists at the
project root, NSmithy synthesizes one from the contracts `ProjectReference`:

```shell
rm HelloWorld.Client/smithy-build.json
```

Then add a `ProjectReference` to `HelloWorld.Client/HelloWorld.Client.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../HelloWorld.Contracts/HelloWorld.Contracts.csproj" />
</ItemGroup>
```

Run the client with the server still running:

```shell
dotnet run --project HelloWorld.Client
# Hello, world!
```

When you're ready to distribute, see
[Distributing Contracts](/smithy-dotnet/guides/distributing-contracts/) to
publish the contracts JAR and switch back to a Maven reference.

## Walk through the code

### The model

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
the `{name}` path segment. All generated code derives from this model; change
the model and rebuild to regenerate it.

### Generated types

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

### The server handler

The generated server uses [ASP.NET Core minimal API](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis).
`Program.cs` registers your handler and maps the routes:

```csharp
builder.Services.AddHelloServiceHandler<HelloHandler>();

app.MapHelloService();
```

The template supplies `HelloHandler` in `Program.cs`; this is your implementation,
not a file regenerated during the build:

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

### The generated client

`HelloWorld.Client/Program.cs` uses the generated typed client:

```csharp
var client = new HelloServiceClient(new Uri("http://localhost:5000"));

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message);
```

The client and server generate their input/output types independently from the
same Smithy model. The client handles serialization and HTTP communication.

## Template options

All three templates accept `--protocol`. Use the same value for all three projects:

| Value | Protocol |
| --- | --- |
| `restJson1` | `aws.protocols#restJson1` (default) |
| `simpleRestJson` | `alloy#simpleRestJson` |
| `rpcv2Cbor` | `smithy.protocols#rpcv2Cbor` |
| `grpc` | `alloy.proto#grpc` (experimental) |

Additional options:

```shell
dotnet new nsmithy-server --help
dotnet new nsmithy-contracts --help
dotnet new nsmithy-client --help
```

## Example repository

For a more complete example with multiple operations, error types, and a working
client/server setup, see
[nsmithy-minimal](https://github.com/thomaslaich/nsmithy-minimal).
