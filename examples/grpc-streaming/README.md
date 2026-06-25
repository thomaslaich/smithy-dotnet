# NSmithy gRPC Streaming Example

This example demonstrates generated NSmithy gRPC event streaming with a small
multi-client chat server. It does not use Grpc.Net, Grpc.Tools, or
protoc-generated C# code.

- `WatchRoom` is server streaming.
- `UploadTranscript` is client streaming.
- `Chat` is bidirectional streaming.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
```

Start the server:

```bash
cd examples/grpc-streaming
dotnet run --project server -- 5002
```

In another shell, run the client:

```bash
cd examples/grpc-streaming
dotnet run --project client -- http://localhost:5002 alice
```

Open another shell and run a second client with another user name. Messages typed
in either client are broadcast to every connected client. Submit an empty line to
disconnect.
