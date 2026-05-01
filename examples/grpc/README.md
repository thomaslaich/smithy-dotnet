# Smithy.NET Dual-Protocol Walking Skeleton

For the full walkthrough, see `docs/multi-protocol.md`.

This example shows one Smithy service exposed over both HTTP and gRPC in the
current Smithy.NET preview.

The model carries both `alloy#simpleRestJson` and `alloy.proto#grpc` on the same
service:

- `simpleRestJson` generates the current HTTP client/server surfaces.
- `grpc` produces `.proto` files that flow into `Grpc.Tools` and generate the
  gRPC client/server base types.
- both transports share the same generated Smithy shapes, descriptors, and
  `IHelloServiceHandler` implementation.

The server registers one handler and maps both generated transport entry points:

- `app.MapHelloServiceHttp()`
- `app.MapHelloServiceGrpc()`

The example uses separate cleartext ports because HTTP/1.1 REST and cleartext
gRPC HTTP/2 cannot reliably share the same endpoint without TLS/ALPN:

- HTTP: `http://localhost:5000`
- gRPC: `http://localhost:5001`

The client example exercises both the generated HTTP client and the generated
gRPC client against the same running server.

## Run

This example assumes you already have the local Smithy.NET packages available,
either from a repository checkout or from published NuGet packages.

Start the dual-protocol server:

```bash
cd examples/grpc/dotnet/server
dotnet run
```

In another shell, run the example client, which calls both transports:

```bash
cd examples/grpc/dotnet/client
dotnet run -- http://localhost:5000 http://localhost:5001 world
```

Current preview note: this is still a walking skeleton. The service is modeled
once, implemented once, and consumed through two transport-specific clients.
