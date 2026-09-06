# Polyglot example

This example demonstrates a generated .NET client calling a Smithy-defined Java
service. The server owns its Smithy model and code-generation setup; the .NET
client consumes that model in its own project.

## Projects

| Project | Language | Port | Role |
| --- | --- | --- | --- |
| `java-service` | Java (Smithy Java) | 8082 | `restJson1` server |
| `dotnet-client` | .NET (NSmithy) | n/a | Generated client for the Java service |

The Java service implements `GET /hello/{name}` and `POST /shout`.

## Model ownership

- **Java:** Smithy Java reads `java/smithy/model/java-hello.smithy`, which uses
  `aws.protocols#restJson1`, and generates server stubs plus request/response
  types. Its Smithy namespace is `example.java.hello`.
- **.NET:** NSmithy reads the Java server model, generates a typed C# client, and
  calls the service.

This mirrors a realistic polyglot setup: services publish their own Smithy
models, and consumers generate local clients for the APIs they use.

## Prerequisites

- .NET 10 SDK
- `just`, or the repository toolchain through `devenv shell`
- Docker for the Docker Compose workflow
- Java and Gradle for running the Java service directly; `devenv shell`
  provides both

## Build

Run all commands in this README from `examples/polyglot`. First build the local
NSmithy packages and .NET client:

```bash
just build
just pack
just refresh-examples
```

## Run locally

Enter the repository development shell, then start the Java service:

```bash
devenv shell
gradle -p java :server:run
```

The Java service generates sources from
`java/smithy/model/java-hello.smithy` before compiling. Adding
an operation makes the generated service builder require a registered
implementation for it.

In another shell, run the generated .NET client:

```bash
dotnet run --project dotnet -- world http://localhost:8082
```

The .NET project runs NSmithy code generation as part of its normal build and
consumes packages from `artifacts/packages`.

## Run with Docker Compose

```bash
docker compose up --build
```

## Try the API

Once the service is running:

```bash
# Ask the Java service to say hello.
curl http://localhost:8082/hello/world

# Ask Java to handle its own ShoutHello operation.
curl -X POST http://localhost:8082/shout \
  -H "Content-Type: application/json" \
  -d '{"name": "world"}'

# Ask the generated .NET client to call the service.
dotnet run --project dotnet -- world http://localhost:8082
```

The commands run on the host and therefore use the published port `8082`.

## Stop

Stop locally run processes with Ctrl+C. For the Docker Compose workflow, remove
the resources with:

```bash
docker compose down
```

## Add a .NET service

The .NET side currently demonstrates generated client code against a Java API.
A .NET service can use the same MSBuild integration with an
`alloy#simpleRestJson` model; see the [simpleRestJson example](../simplerestjson/)
for the generated ASP.NET Core server path.
