# gRPC Streaming Example

This example demonstrates Smithy-modeled gRPC streaming with a small multi-client
chat service.

- `client` and `server` are NSmithy-native: they use the generated NSmithy
  client/server and `NSmithy.Protocols.Grpc`.
- `grpcnet-client` and `grpcnet-server` are Grpc.Net peers generated from the
  `.proto` emitted by `smithy-proto-codegen`.

- `WatchRoom` is server streaming.
- `UploadTranscript` is client streaming.
- `Chat` is bidirectional streaming.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
```

Start either server. Both default to port `5002`.

```bash
cd examples/grpc-streaming
dotnet run --project server
dotnet run --project grpcnet-server
```

In another shell, run either client. The user name is the first argument; the
endpoint or port is optional and comes last. Both clients default to
`http://localhost:5002`.

```bash
cd examples/grpc-streaming
dotnet run --project client -- alice
dotnet run --project grpcnet-client -- bob
```

Open another shell and run a second client with another user name. Messages typed
in either client are broadcast to every connected client. Submit an empty line to
disconnect.

## Grpc.Net Interop

Run the other server on a different port when you want both implementations
running at the same time:

```bash
cd examples/grpc-streaming
dotnet run --project grpcnet-server -- 5003
```

Then connect either client implementation:

```bash
dotnet run --project client -- alice 5003
dotnet run --project grpcnet-client -- bob 5003
dotnet run --project grpcnet-client -- bob 5002
```

The first command connects the NSmithy client to the Grpc.Net server. The second
connects the Grpc.Net client to the Grpc.Net server. The third connects the
Grpc.Net client to the NSmithy server on port `5002`.
