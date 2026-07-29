# rpcv2Cbor Streaming Example

This example demonstrates Smithy-modeled rpcv2Cbor event streaming with a small
multi-client chat service.

- `WatchRoom` is server streaming.
- `UploadTranscript` is client streaming.
- `Chat` is bidirectional streaming.

## Run

From the repository root, build and pack local packages:

```bash
just build
just pack
```

Start the server. It defaults to port `5004` and serves cleartext HTTP/2 so the
duplex stream can send and receive messages at the same time.

```bash
cd examples/rpcv2cbor-streaming
dotnet run --project server
```

In another shell, run a client. The user name is the first argument; the endpoint
or port is optional and comes last. The client defaults to
`http://localhost:5004`.

```bash
cd examples/rpcv2cbor-streaming
dotnet run --project client -- alice
```

Open another shell and run a second client with another user name. Messages typed
in either client are broadcast to every connected client. Submit an empty line to
disconnect.
