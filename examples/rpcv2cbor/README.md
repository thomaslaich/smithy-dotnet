# NSmithy rpcv2Cbor Example

A minimal greeting service built with `smithy.protocols#rpcv2Cbor`. Demonstrates a
generated ASP.NET Core server and a generated typed client communicating over CBOR.

- `contracts`: the Smithy model, packaged as a contracts project.
- `server`: generated ASP.NET Core endpoint with a handwritten `IHelloServiceHandler`
  implementation.
- `client`: generated typed client that connects to the server.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
just refresh-examples
```

Start the server in one terminal:

```bash
cd examples/rpcv2cbor/server
dotnet run --urls http://localhost:5001
```

Run the client in another terminal:

```bash
cd examples/rpcv2cbor/client
dotnet run -- http://localhost:5001 world
```

You should see:

```
Hello response from rpcv2cbor-server: Hello, world!
```

To exercise the error path:

```bash
dotnet run -- http://localhost:5001 error
```

```
Server rejected name: name must not be 'error'
```
