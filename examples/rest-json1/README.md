# NSmithy restJson1 Example

A Weather service built with `aws.protocols#restJson1`. The model is adapted from
the [Smithy quickstart](https://smithy.io/2.0/quickstart.html) and demonstrates
resources, pagination, errors, retries (`@retryable`), HTTP binding traits, and
end-to-end OpenTelemetry observability using the AWS REST JSON protocol.

- `contracts`: the Smithy model, packaged as a contracts project.
- `server`: generated ASP.NET Core endpoints with a handwritten `IWeatherServiceHandler` implementation that supports real server-side pagination.
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
cd examples/rest-json1
pixi shell  # not needed when using direnv
dotnet run --project server --urls http://localhost:5000
```

In another shell, run the client:

```bash
cd examples/rest-json1
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
