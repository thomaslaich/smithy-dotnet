# Smithy.NET AWS Protocol Example

This example now groups the current AWS-focused client paths in one place.

It is intentionally client-only and currently demonstrates:

- `smithy.protocols#rpcv2Cbor` through a generated typed client and a mock peer
- `aws.protocols#restXml` through a generated typed client and the same mock peer

Both `smithy.protocols#rpcv2Cbor` and `aws.protocols#restXml` now come from the
normal Smithy trait dependencies configured for the example.

## Run

From the repository root, create local packages:

```bash
just build
just pack
just refresh-examples
```

Then run the example client:

```bash
cd examples/aws/client
dotnet run -- world
```

That runs both the `rpcv2Cbor` and `restXml` clients against the in-process
mock transport.

To exercise the generated `rpcv2Cbor` error path:

```bash
cd examples/aws/client
dotnet run -- error
```

Preview note: this is still a narrow example. It demonstrates generated client
code, CBOR request/response bodies with `__type`-based error decoding, and a
basic generated `restXml` client path.
