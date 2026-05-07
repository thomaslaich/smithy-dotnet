---
title: RPC v2 CBOR
description: smithy.protocols#rpcv2Cbor — binary CBOR encoding over HTTP/2. Client-only, early preview.
---

`smithy.protocols#rpcv2Cbor` is Smithy's binary protocol. Messages are encoded
as [CBOR](https://cbor.io/) and carried over HTTP/2 POST requests on a fixed
path derived from the service and operation names. Status: **Early preview,
client-only**.

## Maven Dependency

No extra Maven dependency beyond the codegen plugin — `smithy.protocols` shapes
are bundled with the Smithy CLI.

## NuGet Package

```xml
<PackageReference Include="NSmithy.Codecs.Cbor" Version="0.1.0-preview.8" />
```

## Modeling

Apply `@rpcv2Cbor` to the service. Operations do not carry `@http` traits —
the protocol maps each operation to a fixed
`POST /service/{Service}/operation/{Operation}` path automatically:

```smithy
$version: "2"

namespace example.hello

use smithy.protocols#rpcv2Cbor

@rpcv2Cbor
service HelloService {
    version: "2026-01-01"
    operations: [SayHello]
}

operation SayHello {
    input := {
        @required
        name: String
    }
    output := {
        @required
        message: String
    }
    errors: [InvalidName]
}

@error("client")
structure InvalidName {
    message: String
}
```

## Client

The CBOR codec is wired up automatically by the generated client — no manual
configuration is required:

```csharp
using Example.Hello;
using NSmithy.Client;

var client = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri("https://api.example.com") }
);

try
{
    var response = await client.SayHelloAsync(new SayHelloInput("world"));
    Console.WriteLine(response.Message);
}
catch (InvalidNameException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

## Related

- [Protocol Status](/smithy-dotnet/protocols/) — coverage overview and what "early preview" means
- [Conformance Tests](/smithy-dotnet/protocols/conformance/) — how correctness is verified
