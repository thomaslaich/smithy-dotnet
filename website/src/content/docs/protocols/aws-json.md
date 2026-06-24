---
title: AWS JSON
description: aws.protocols#awsJson1_1 and aws.protocols#awsJson1_0 — client-only JSON RPC for AWS-compatible services.
---

`aws.protocols#awsJson1_1` and `aws.protocols#awsJson1_0` are AWS JSON-RPC
protocols. NSmithy generates typed clients for both through
`AwsJson11Protocol` and `AwsJson10Protocol`. Status: **Early preview,
client-only**.

The currently verified slice covers `X-Amz-Target`, AWS JSON content types,
empty input/output handling, special floating-point values, and common
client-side error discriminator formats. `awsJson1_0` has runtime support but no
separate conformance project yet.

See [Protocol Status](/smithy-dotnet/protocols/status/) for current conformance
numbers.

## Maven Dependency

```json
"software.amazon.smithy:smithy-aws-traits:1.68.0"
```

## NuGet Package

```xml
<PackageReference Include="NSmithy.Protocols.AwsJson" Version="0.3.0" />
```

## Modeling

Apply the AWS JSON protocol trait to the service:

```smithy
$version: "2"

namespace example.jsonrpc

use aws.protocols#awsJson1_1

@awsJson1_1
service ControlPlane {
    version: "2026-01-01"
    operations: [GetWidget]
}

operation GetWidget {
    input := {
        id: String
    }
    output := {
        name: String
    }
}
```

## Client

The generated client selects the declared protocol by default:

```csharp
using Example.Jsonrpc;

var client = new ControlPlaneClient(new Uri("https://api.example.com"));
var response = await client.GetWidgetAsync(new GetWidgetInput("abc"));
```

AWS JSON support is client-only. NSmithy does not generate AWS JSON servers and
does not yet provide AWS SDK-style endpoint resolution, credential chains,
retries, or pagination helpers. Explicit SigV4 signing exists in early preview;
see [Authentication](/smithy-dotnet/guides/authentication/).
