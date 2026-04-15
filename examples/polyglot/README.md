# Polyglot Example

This example demonstrates a Smithy-defined service implemented in multiple languages.
Each service owns its own Smithy model and code generation setup. The models
intentionally expose the same HTTP routes and JSON payloads so the services can
call each other, but they are not treated as a shared source of truth.

## Services

| Service | Language | Port | Role |
|---------|----------|------|------|
| `scala-service` | Scala (Smithy4s + http4s) | 8081 | Reference server implementation |
| `java-service` | Java (Smithy Java) | 8082 | Generated server implementation |

Each service implements `HelloService`:
- `GET /hello/{name}` — returns a greeting and the name of the responding service
- `POST /ping` — calls another service's `/hello` as a client and forwards the response

## Model

The services use separate Smithy models:

- **Scala**: Smithy4s reads `scala/model/hello.smithy`, which uses
  `alloy#simpleRestJson`, and generates server routes, client stubs, and
  request/response types at compile time.
- **Java**: Smithy Java reads `java/smithy/model/hello.smithy`, which uses
  `aws.protocols#restJson1`, and generates server stubs, request/response types,
  and a typed client used by the `Ping` operation.

This mirrors a more realistic polyglot setup: services do not usually share a
single in-repo Smithy file. They publish compatible HTTP APIs and generate local
code from their own model.

## Development

### Prerequisites

Use the repo's devenv shell — it provides `sbt`, `gradle`, and the Smithy CLI:

```bash
devenv shell
```

### Scala — compile and run

```bash
cd scala
sbt compile   # regenerates Scala types from scala/model/hello.smithy
sbt run       # starts the server on port 8080
```

After editing `scala/model/hello.smithy`, run `sbt compile` again. Smithy4s
regenerates all types, server routes, and client stubs. If you add a new
operation, the compiler will tell you exactly which methods are missing from the
`HelloService` implementation.

### Java — run locally

```bash
cd java
gradle :server:run
```

The Java service generates sources from `java/smithy/model/hello.smithy` before
compiling. If you add a new operation, the generated service builder will require
that the server registers an implementation for it.

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

# Ask Scala to ping Java (Scala acts as client, Java as server)
curl -X POST http://localhost:8081/ping \
  -H "Content-Type: application/json" \
  -d '{"targetUrl": "http://java-service:8080", "name": "world"}'

# Ask Java to ping Scala (Java acts as client, Scala as server)
curl -X POST http://localhost:8082/ping \
  -H "Content-Type: application/json" \
  -d '{"targetUrl": "http://scala-service:8080", "name": "world"}'
```

The outer `curl` runs on the host, so it uses the published ports (`8081` and
`8082`). The `targetUrl` is consumed inside the container that handles `/ping`,
so it uses Docker Compose service DNS and the container port (`8080`).

## Adding the .NET service

When Smithy.NET is ready, a `dotnet/` service will be added here. It will use the
MSBuild integration to generate C# types, server handlers, and a typed HTTP
client from its own model, while matching the same HTTP routes and payloads.
