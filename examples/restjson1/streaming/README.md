# restJson1 Streaming Example

This example demonstrates Smithy-modeled restJson1 event streaming with the same
small multi-client chat service as the rpcv2Cbor and gRPC streaming examples.

- `WatchRoom` is server streaming.
- `UploadTranscript` is client streaming.
- `Chat` is bidirectional streaming and carries the room as initial metadata
  around the event stream.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
```

Start the server. It defaults to port `5005` and serves cleartext HTTP/2 so the
duplex stream can send and receive messages at the same time.

```bash
cd examples/restjson1/streaming
dotnet run --project server
```

In another shell, run a client. The user name is the first argument; the endpoint
or port is optional and comes last. The client defaults to
`http://localhost:5005`.

```bash
cd examples/restjson1/streaming
dotnet run --project client -- alice
```

Open another shell and run a second client with another user name. Messages typed
in either client are broadcast to every connected client. Submit an empty line to
disconnect.
