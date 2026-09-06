# NSmithy restJson1 Example

A Weather service built with `aws.protocols#restJson1`. The model is adapted from
the [Smithy quickstart](https://smithy.io/2.0/quickstart.html) and demonstrates
resources, pagination, errors, retries (`@retryable`), HTTP binding traits, and
end-to-end OpenTelemetry observability using the AWS REST JSON protocol. The
same generated operations and Smithy prompt templates are also exposed over MCP.

- `contracts`: the Smithy model, packaged as a contracts project.
- `server`: generated ASP.NET Core endpoints and an MCP stdio server backed by a handwritten `IWeatherServiceHandler` implementation that supports real server-side pagination.
- `client`: generated typed client that pages through all cities using the `nextToken` continuation token.

The server and client reference the contracts project directly. No
`smithy-build.json` is needed — NSmithy synthesizes one from the model sources
and Maven dependencies declared in the contracts project.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
just refresh-examples
```

Start the server:

```bash
cd examples/restjson1
dotnet run --project server --urls http://localhost:5000
```

In another shell, run the client:

```bash
cd examples/restjson1
dotnet run --project client -- http://localhost:5000
```

With the server running, open in your browser:

| Route | Description |
|-------|-------------|
| [`/openapi`](http://localhost:5000/openapi) | Scalar interactive API explorer |
| [`/docs`](http://localhost:5000/docs) | smithy-docgen generated documentation |

Or call the server directly:

```bash
curl -i http://localhost:5000/current-time
curl -i 'http://localhost:5000/cities?pageSize=3'
curl -i 'http://localhost:5000/cities?pageSize=3&nextToken=CHI'
curl -i http://localhost:5000/cities/SEA
curl -i http://localhost:5000/cities/SEA/forecast
curl -i http://localhost:5000/cities/SEA/flaky-forecast   # 503s two of every three calls
```

## MCP tools and prompts

Run the same Weather service as an MCP stdio server by passing `--mcp`:

```bash
dotnet run --no-build --project examples/restjson1/server -- --mcp
```

This is a separate runtime mode: the process hosts MCP over stdio and does not
start the ASP.NET Core endpoints. Build it first because build output on stdout
would interfere with the MCP message stream.

Configure an MCP client to launch that built assembly (replace the repository
path with an absolute path):

```json
{
  "mcpServers": {
    "weather": {
      "command": "dotnet",
      "args": [
        "/path/to/smithy-dotnet/examples/restjson1/server/bin/Debug/net10.0/NSmithy.Examples.RestJson1.Server.dll",
        "--mcp"
      ]
    }
  }
}
```

For [Claude Code](https://docs.anthropic.com/en/docs/claude-code/mcp), add the
built server from the repository root:

```bash
claude mcp add weather -- dotnet /absolute/path/to/smithy-dotnet/examples/restjson1/server/bin/Debug/net10.0/NSmithy.Examples.RestJson1.Server.dll --mcp
```

Or let Claude Code launch the project through `dotnet run`. Keep `--no-build`:
build output on stdout would corrupt the MCP stdio stream.

```bash
claude mcp add weather -- dotnet run --no-build --project /absolute/path/to/smithy-dotnet/examples/restjson1/server/NSmithy.Examples.RestJson1.Server.csproj -- --mcp
```

Check the registration with `claude mcp get weather`. Inside Claude Code, `/mcp`
shows the connection, and the generated prompts are available as
`/mcp__weather__city_weather_brief SEA` and
`/mcp__weather__forecast_answer SEA`.

The generated `GetCurrentTime`, `GetCity`, `ListCities`, `GetForecast`, and
`GetFlakyForecast` operations appear as tools. Their descriptions, input and
output schemas, validation, and read-only hints all come from the Smithy model.
The model also contributes two prompts:

- `city_weather_brief` guides the model to call both `GetCity` and `GetForecast`
  before producing a combined summary.
- `forecast_answer` guides the model to call `GetForecast` and explain its
  numeric chance of rain in plain language.

Both prompts accept a required `cityId` argument. A prompt template does not
call a handler itself; the MCP client expands it into instructions for the model,
which then chooses and invokes the named tools. The server exposes the tools and
prompts together through `.WithSmithyService(WeatherSchema.Schema)`.

The ASP.NET Core and MCP hosts share the same `WeatherHandler`; only the
transport adapter changes.

## Rejecting a bad request

The server enforces the model at the boundary, so a request that violates it is
answered with a structured 4xx rather than reaching the handler. Nothing in the
handler implements these checks — they are generated from the model.

`CityId` is declared `@pattern("^[A-Za-z0-9 ]+$")`, so a city ID carrying a
character the pattern excludes is rejected with `ValidationException`, which
names the member and the constraint it failed:

```bash
curl -i 'http://localhost:5000/cities/SEA%21'   # "SEA!"
```

```
HTTP/1.1 400 Bad Request
X-Amzn-Errortype: ValidationException

{"message":"1 validation error detected. Value at '/cityId' failed to satisfy
constraint: Member must satisfy regular expression pattern: ^[A-Za-z0-9 ]+$",
"fieldList":[{"path":"/cityId","message":"..."}]}
```

Input that cannot become modeled input at all fails earlier, during
deserialization, and comes back as `SerializationException` — here `pageSize` is
modeled as `Integer`:

```bash
curl -i 'http://localhost:5000/cities?pageSize=abc'
```

```
HTTP/1.1 400 Bad Request
X-Amzn-Errortype: SerializationException

{"message":"Value 'abc' is not a valid integer."}
```

An `Accept` header that excludes the response's media type is answered with 406
before the operation runs:

```bash
curl -i -H 'Accept: application/xml' http://localhost:5000/current-time
```

```
HTTP/1.1 406 Not Acceptable
X-Amzn-Errortype: NotAcceptableException

{"message":"Response is 'application/json', which Accept 'application/xml' excludes."}
```

A request that satisfies the model but names something absent still reaches the
handler and returns the error the operation models — a space is inside
`CityId`'s pattern, so this is a `NoSuchResource`, not a validation failure:

```bash
curl -i 'http://localhost:5000/cities/%20'
```

```
HTTP/1.1 400 Bad Request
X-Amzn-Errortype: NoSuchResource

{"resourceType":"City"}
```

## Observability

Both the client and the server export OpenTelemetry traces and metrics over
OTLP to `http://localhost:4317`. Start
[grafana/otel-lgtm](https://github.com/grafana/docker-otel-lgtm) (Grafana +
Tempo + Prometheus + Loki in one container) before running the example:

```bash
docker run --rm -p 3000:3000 -p 4317:4317 -p 4318:4318 grafana/otel-lgtm
```

Then run the server and client as above and open Grafana at
[http://localhost:3000](http://localhost:3000) (anonymous admin login).

**Traces** (Explore → Tempo): each client operation is a `weather-client` span
named `Weather.{Operation}` with one child span per transport attempt; the
server's ASP.NET Core spans join the same trace via W3C context propagation.
The interesting ones are the `Weather.GetFlakyForecast` traces — the server
fails two of every three calls with the model's `@retryable`
`ServiceUnavailable` error (HTTP 503), and the client's
`SmithyStandardRetryStrategy` retries with exponential backoff, so each trace
shows failed attempt spans (status `error`, `error.type` set) followed by a
successful one, with the backoff visible as gaps between attempts.

**Metrics** (Explore → Prometheus): the client runtime emits, dimensioned by
`rpc_service` / `rpc_method`:

| Metric | Meaning |
|--------|---------|
| `smithy_client_operation_duration_seconds` | End-to-end duration, spanning all attempts and backoff. |
| `smithy_client_attempts_total` | Transport attempts, including retries — divide by call count to see retry amplification. |
| `smithy_client_errors_total` | Failed executions, dimensioned by `error_type`. |

The wiring is minimal: the client subscribes its tracer/meter provider to the
`NSmithy.Client` source and meter (see `client/Program.cs`); the server uses
standard ASP.NET Core instrumentation (see `server/Program.cs`).
