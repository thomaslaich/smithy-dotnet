# NSmithy AWS Protocol Example

A minimal example demonstrating `aws.protocols#restXml` through a generated typed client
with a mock peer.

## Run

From the repository root, create local packages:

```bash
just build
just pack
just refresh-examples
```

Then run the example:

```bash
cd examples/aws/client
dotnet run -- world
```

You should see:

```
SayHelloXml => Hello, world! from mock-restxml
```

## Structure

The model is defined directly in `model/hello.smithy`. The client project references
the shared `smithy-build.json` and generates a typed client for `HelloXmlService`.

For an `rpcv2Cbor` example with a real ASP.NET Core server, see
[`examples/rpcv2cbor`](../rpcv2cbor/).
