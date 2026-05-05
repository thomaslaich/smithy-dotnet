---
title: AWS Protocols
description: Use binary (CBOR) and XML protocol clients generated from Smithy models.
---

`smithy.protocols#rpcv2Cbor` and `aws.protocols#restXml` are both early-preview
client-only protocols in NSmithy. This guide shows how to set them up and use
the generated clients.

## rpcv2Cbor

`rpcv2Cbor` is Smithy's binary protocol — all messages are encoded as
[CBOR](https://cbor.io/) and carried over HTTP/2 POST requests. It is used by
newer AWS service endpoints and is a good fit for internal services where binary
encoding reduces payload size.

### Model

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

`rpcv2Cbor` operations have no `@http` trait — the protocol maps each operation
to a fixed `POST /service/{Service}/operation/{Operation}` path.

### Client

Add `NSmithy.Codecs.Cbor` to your project:

```xml
<PackageReference Include="NSmithy.Codecs.Cbor" Version="0.1.0-preview.8" />
```

Use the generated client the same way as any other NSmithy client:

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

The codec is wired up automatically by the generated client — no manual
configuration is required.

## restXml

`aws.protocols#restXml` is the XML-over-HTTP protocol used by AWS services such
as S3 and Route 53. NSmithy generates a typed client that encodes request bodies
as XML and decodes XML responses.

### Model

```smithy
$version: "2"

namespace example.hello

use aws.protocols#restXml

@restXml
service HelloXmlService {
    version: "2026-01-01"
    operations: [SayHello]
}

@http(method: "POST", uri: "/hello")
operation SayHello {
    input := {
        @required
        name: String
    }
    output := {
        @required
        @xmlName("Message")
        message: String
    }
}
```

`@xmlName` overrides the XML element name for a member. Without it the member
name is used as-is.

### Client

Add `NSmithy.Codecs.Xml` to your project:

```xml
<PackageReference Include="NSmithy.Codecs.Xml" Version="0.1.0-preview.8" />
```

```csharp
using Example.Hello;
using NSmithy.Client;

var client = new HelloXmlServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri("https://api.example.com") }
);

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message);
```

## Related

- [Protocol Status](/smithy-dotnet/protocols/) — current coverage and what "early preview" means
- [Conformance Tests](/smithy-dotnet/protocols/conformance/) — how protocol correctness is verified
