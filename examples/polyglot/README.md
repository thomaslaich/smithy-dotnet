# Polyglot Example

This example demonstrates one .NET app calling a Smithy-defined service from
another ecosystem. The server owns its own Smithy model and code generation
setup; the .NET client consumes that model in its own project.

## Services

| Service | Language | Port | Role |
|---------|----------|------|------|
| `java-service` | Java (Smithy Java) | 8082 | `restJson1` server |
| `dotnet-client` | .NET (NSmithy) | n/a | Generated client for the service |

The Java service implements `GET /hello/{name}` and `POST /shout`.

## Model

- **Java**: Smithy Java reads `java/smithy/model/java-hello.smithy`, which uses
  `aws.protocols#restJson1`, and generates server stubs plus request/response
  types. Its Smithy namespace is `example.java.hello`.
- **.NET client**: NSmithy reads the Java server model, generates a typed C#
  client, and calls the service.

This mirrors a realistic polyglot setup: services do not usually share a single
in-repo Smithy file. They publish their own Smithy models, and consumers generate
local clients for the APIs they use.

## Development

### Prerequisites

Use the repo's devenv shell — it provides `gradle` and the Smithy CLI:

```bash
devenv shell
```

### Java — run locally

```bash
cd java
gradle :server:run
```

The Java service generates sources from `java/smithy/model/java-hello.smithy` before
compiling. If you add a new operation, the generated service builder will require
that the server registers an implementation for it.

### .NET — run the generated client

First create local NuGet packages from the repository root:

```bash
dotnet pack NSmithy.slnx --configuration Release --output artifacts/packages
```

Then run the client against the service:

```bash
cd dotnet
dotnet run -- world http://localhost:8082
```

The .NET example restores packages from `artifacts/packages` first. It uses the
MSBuild package to run the Smithy CLI, generate C# into `obj/`, and compile the
typed client as part of the normal build.

### Run everything with Docker Compose

```bash
docker compose up --build
```

## Try it out

Once the service is running:

```bash
# Ask the Java service to say hello
curl http://localhost:8082/hello/world

# Ask Java to handle its own ShoutHello operation
curl -X POST http://localhost:8082/shout \
  -H "Content-Type: application/json" \
  -d '{"name": "world"}'

# Ask the generated .NET client to call the service
cd dotnet
dotnet run -- world http://localhost:8082
```

The outer `curl` and .NET client run on the host, so they use the published
port (`8082`).

## Adding the .NET service

The .NET side currently demonstrates generated client code against a Java API.
A .NET service can use the same MSBuild integration with an
`alloy#simpleRestJson` model; see `examples/simple-rest-json` for the
generated ASP.NET Core server path.
