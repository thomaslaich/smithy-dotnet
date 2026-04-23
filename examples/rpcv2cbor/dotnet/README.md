# Smithy.NET rpcv2Cbor Example

This example shows the current `smithy.protocols#rpcv2Cbor` client preview in
Smithy.NET.

It is intentionally client-only:

- the Smithy model generates a typed .NET client
- a handwritten `HttpMessageHandler` acts as an in-process mock peer
- requests and responses use the shared CBOR codec runtime

The example also carries a tiny local definition of `smithy.protocols#rpcv2Cbor`
so Smithy CLI validation does not depend on external protocol model packages.

That keeps the example focused on the new transport/codec seam without
pretending that server generation exists yet.

## Run

From the repository root, create local packages:

```bash
just build
just pack
just refresh-examples
```

Then run the example client:

```bash
cd examples/rpcv2cbor/dotnet/client
dotnet run -- world
```

To exercise the generated error path:

```bash
cd examples/rpcv2cbor/dotnet/client
dotnet run -- error
```

Preview note: this is still a narrow `rpcv2Cbor` slice. The example currently
demonstrates generated client code, CBOR request/response bodies, and
`__type`-based error decoding through a mock transport.
