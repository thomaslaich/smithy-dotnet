# NSmithy simpleRestJson Example

A Weather service built with `alloy#simpleRestJson`. The model is adapted from
the [Smithy quickstart](https://smithy.io/2.0/quickstart.html) and demonstrates
resources, pagination, errors, and HTTP binding traits.

- `contracts`: the Smithy model, packaged as a contracts project.
- `server`: generated ASP.NET Core endpoints with a handwritten `IWeatherServiceHandler` implementation.
- `client`: generated typed client that calls the server.

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
cd examples/simple-rest-json
pixi shell  # not needed when using direnv
dotnet run --project server --urls http://localhost:5000
```

In another shell, run the client:

```bash
cd examples/simple-rest-json
dotnet run --project client -- http://localhost:5000
```

Or call the server directly:

```bash
curl -i http://localhost:5000/current-time
curl -i http://localhost:5000/cities
curl -i http://localhost:5000/cities/SEA
curl -i http://localhost:5000/cities/SEA/forecast
```
