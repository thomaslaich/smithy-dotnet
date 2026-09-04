# rpcv2Cbor streaming example

This example demonstrates Smithy-modeled rpcv2Cbor event streaming with a small
multi-client chat service.

- `WatchRoom` is server streaming.
- `UploadTranscript` is client streaming.
- `Chat` is bidirectional streaming and carries the room as initial metadata
  around the event stream.

## Projects

- `contracts`: the Smithy chat model shared by the client and server.
- `server`: generated ASP.NET Core endpoints backed by an in-memory chat room.
- `client`: generated typed client for all three streaming operations.

## Prerequisites

- .NET 10 SDK
- `just`, or the repository toolchain through `devenv shell`

## Build

Run all commands in this README from the repository root. First build the local
packages and examples:

```bash
just build
just pack
just refresh-examples
```

## Run

Start the server. It defaults to port `5004` and serves cleartext HTTP/2 so the
duplex stream can send and receive messages at the same time.

```bash
dotnet run --project examples/rpcv2cbor/streaming/server
```

In another shell, run a client. The user name is the first argument; the endpoint
or port is optional and comes last. The client defaults to
`http://localhost:5004`.

```bash
dotnet run --project examples/rpcv2cbor/streaming/client -- alice
```

Open another shell and run a second client with another user name. Messages typed
in either client are broadcast to every connected client. Submit an empty line to
disconnect.
