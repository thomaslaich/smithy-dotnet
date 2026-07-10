# NSmithy Native gRPC Example

A library management service built with `alloy.proto#grpc` and served over
NSmithy's native gRPC stack. There is no protoc, `Grpc.Tools`, `Grpc.Net`, or
`Google.Protobuf` in this example: the generated client and server speak the
gRPC wire protocol (HTTP/2 + protobuf) through NSmithy's schema-driven proto
codec, the same way the other examples speak JSON or CBOR. See
[designs/native-grpc.md](../../designs/native-grpc.md) for the design.

The model exercises the proto codec's feature surface: `@protoIndex` field
numbers, `@protoNumType` (`uint32`, `fixed64`), `@sparse` maps,
`@protoInlinedOneOf` unions, `intEnum`, and string enums.

- `contracts`: the Smithy model, packaged as a contracts project.
- `server`: ASP.NET Core server on Kestrel HTTP/2 that maps the generated gRPC
  endpoints via `MapLibraryServiceGrpc()`.
- `client`: generated typed client that selects gRPC with
  `Protocol = new GrpcProtocol()`.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
just refresh-examples
```

Start the server (listens on `http://localhost:5001`, HTTP/2 cleartext):

```bash
cd examples/grpc
dotnet run --project server
```

In another shell, run the client:

```bash
cd examples/grpc
dotnet run --project client -- http://localhost:5001
```

The client exercises every operation: get, create, list with a
`@protoInlinedOneOf` filter, search, batch upload, and delete.

## Interop

NSmithy peers speak standard gRPC, so either side can be swapped for a
conventional `Grpc.Net` implementation generated from the emitted `.proto`
file. The [grpc-streaming](../grpc-streaming/) example does exactly that,
running NSmithy and `Grpc.Net` peers against each other over the same wire.
