---
title: AWS restXml
description: aws.protocols#restXml — XML over HTTP. Client-only, early preview.
---

`aws.protocols#restXml` is the XML-over-HTTP protocol used by AWS services such
as S3 and Route 53. NSmithy generates a typed client that encodes request bodies
as XML and decodes XML responses. Status: **Early preview, client-only**.

The passing slice is weighted toward response deserialization. Request binding
coverage is still narrow and is expected to grow behind the conformance
allowlist.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

```json
"software.amazon.smithy:smithy-aws-traits:1.56.0"
```

## NuGet Package

```xml
<PackageReference Include="NSmithy.Codecs.Xml" Version="0.3.0" />
```

## Modeling

Apply `@restXml` to the service and `@http` to each operation, the same as
`simpleRestJson`. Use `@xmlName` to override the XML element name for a member:

```smithy
$version: "2"

namespace example.hello

use aws.protocols#restXml

@restXml
service HelloService {
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

`@xmlName` overrides the element name in the serialized XML. Without it the
member name is used as-is.

HTTP binding traits (`@httpLabel`, `@httpQuery`, `@httpHeader`, `@httpPayload`)
work the same way as in `simpleRestJson` — members without an explicit binding
go into the XML body.

## Client

The XML codec is wired up automatically by the generated client:

```csharp
using Example.Hello;

var client = new HelloServiceClient(new Uri("https://api.example.com"));

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message);
```

Explicit SigV4 signing exists in early preview; see
[Authentication](/smithy-dotnet/guides/client-configuration/authentication/). For production calls to
AWS XML services such as S3, prefer the official AWS SDK for .NET until NSmithy's
AWS auth, endpoint resolution, retries, and pagination support mature.
