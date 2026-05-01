# Polyglot Example

This example demonstrates one .NET app calling Smithy-defined services from two
other ecosystems. Each server owns its own Smithy model and code generation
setup; the .NET client consumes both models in the same project.

## Services

| Service | Language | Port | Role |
|---------|----------|------|------|
| `scala-service` | Scala (Smithy4s + http4s) | 8081 | `simpleRestJson` server |
| `java-service` | Java (Smithy Java) | 8082 | `restJson1` server |
| `dotnet-client` | .NET (NSmithy) | n/a | Generated clients for both services |

Both services implement `GET /hello/{name}`. The models are intentionally not
identical:

- Java also implements `POST /shout`.
- Scala also implements `POST /ping`.

## Model

The services use separate Smithy models:

- **Scala**: Smithy4s reads `scala/model/scala-hello.smithy`, which uses
  `alloy#simpleRestJson`, and generates server routes plus request/response
  types at compile time. Its Smithy namespace is `example.scala.hello`.
- **Java**: Smithy Java reads `java/smithy/model/java-hello.smithy`, which uses
  `aws.protocols#restJson1`, and generates server stubs plus request/response
  types. Its Smithy namespace is `example.java.hello`.
- **.NET client**: NSmithy reads both server models, generates two typed C#
  clients, and calls both services from one app.

This mirrors a realistic polyglot setup: services do not usually share a single
in-repo Smithy file. They publish their own Smithy models, and consumers generate
local clients for the APIs they use.

## Development

### Prerequisites

Use the repo's devenv shell — it provides `sbt`, `gradle`, and the Smithy CLI:

```bash
devenv shell
```

### Scala — compile and run

```bash
cd scala
sbt compile   # regenerates Scala types from scala/model/scala-hello.smithy
sbt run       # starts the server on port 8080
```

After editing `scala/model/scala-hello.smithy`, run `sbt compile` again. Smithy4s
regenerates all types, server routes, and client stubs. If you add a new
operation, the compiler will tell you exactly which methods are missing from the
`HelloService` implementation.

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

Then run the client against both services:

```bash
cd dotnet
dotnet run -- world http://localhost:8082 http://localhost:8081
```

The .NET example restores packages from `artifacts/packages` first. It uses the
MSBuild package to run the Smithy CLI, generate C# into `obj/`, and compile the
typed clients as part of the normal build.

Current preview note: `simpleRestJson` services generate both client and server
surfaces, so the .NET consumer project also references the server runtime
packages.

### Run everything with Docker Compose

```bash
docker compose up --build
```

## Try it out

Once both services are running:

```bash
# Ask the Scala service to say hello
curl http://localhost:8081/hello/world

# Ask the Java service to say hello
curl http://localhost:8082/hello/world

# Ask Scala to handle its own Ping operation
curl -X POST http://localhost:8081/ping \
  -H "Content-Type: application/json" \
  -d '{"name": "world"}'

# Ask Java to handle its own ShoutHello operation
curl -X POST http://localhost:8082/shout \
  -H "Content-Type: application/json" \
  -d '{"name": "world"}'

# Ask the generated .NET clients to call both services
cd dotnet
dotnet run -- world http://localhost:8082 http://localhost:8081
```

The outer `curl` and .NET client run on the host, so they use the published
ports (`8081` and `8082`).

## Adding the .NET service

The .NET side currently demonstrates generated client code against Java and
Scala APIs. A .NET service can use the same MSBuild integration with an
`alloy#simpleRestJson` model; see `examples/simple-rest-json/dotnet` for the
generated ASP.NET Core server path.
