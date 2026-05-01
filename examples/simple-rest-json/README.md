# NSmithy simpleRestJson Example

This example uses NSmithy for both sides of an `alloy#simpleRestJson` HTTP
API:

- `server`: generated ASP.NET Core endpoints and a handwritten
  `IHelloServiceHandler` implementation.
- `client`: generated typed client that calls the server.

The model includes a small local definition of `alloy#simpleRestJson` so the
Smithy CLI can validate the example without downloading extra Smithy model
dependencies.

## Run

From the repository root, create local packages:

```bash
just build
just pack
just refresh-examples
```

`just refresh-examples` clears the example projects' local `NSmithy.*`
package cache. This matters while developing with a fixed preview version,
because NuGet otherwise keeps using the older extracted package contents.

Start the server:

```bash
cd examples/simple-rest-json/dotnet/server
dotnet run --urls http://localhost:5000
```

In another shell, run the client:

```bash
cd examples/simple-rest-json/dotnet/client
dotnet run -- http://localhost:5000 world
```

You can also call the server directly:

```bash
curl -i http://localhost:5000/hello/world
curl -i -X POST http://localhost:5000/ping \
  -H "Content-Type: application/json" \
  -d '{"name":"world"}'
```

Current preview note: `simpleRestJson` services generate both client and server
surfaces, so both example projects reference the client and server runtime
packages.
