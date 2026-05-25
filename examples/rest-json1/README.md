# NSmithy restJson1 Example

A Weather service built with `aws.protocols#restJson1`. The model is adapted from
the [Smithy quickstart](https://smithy.io/2.0/quickstart.html) and demonstrates
resources, pagination, errors, and HTTP binding traits using the AWS REST JSON protocol.

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

Or call the server directly:

```bash
curl -i http://localhost:5000/current-time
curl -i 'http://localhost:5000/cities?pageSize=3'
curl -i 'http://localhost:5000/cities?pageSize=3&nextToken=CHI'
curl -i http://localhost:5000/cities/SEA
curl -i http://localhost:5000/cities/SEA/forecast
```
