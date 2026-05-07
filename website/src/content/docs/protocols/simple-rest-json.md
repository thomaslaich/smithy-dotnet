---
title: REST JSON
description: JSON over HTTP with REST bindings — alloy#simpleRestJson and aws.protocols#restJson1.
---

Two Smithy protocols share the same JSON-over-HTTP wire format with REST
bindings:

| Protocol | Trait | Status |
| --- | --- | --- |
| `alloy#simpleRestJson` | `@simpleRestJson` | Preview — client + server |
| `aws.protocols#restJson1` | `@restJson1` | Early preview — client only |

`alloy#simpleRestJson` is the primary path in NSmithy: it has the broadest
coverage, generates both client and server surfaces, and is the recommended
starting point. `aws.protocols#restJson1` is available for consuming AWS-style
REST/JSON services from a generated client; server generation is out of scope
for this preview.

The modeling syntax, HTTP binding traits, and generated C# shapes are identical
between the two protocols — the only difference is the trait applied to the
service shape and the Maven dependency that brings it in.

## Maven Dependencies

`alloy#simpleRestJson` requires `alloy-core`:

```json
"com.disneystreaming.alloy:alloy-core:0.3.38"
```

`aws.protocols#restJson1` requires `smithy-aws-traits` instead:

```json
"software.amazon.smithy:smithy-aws-traits:1.56.0"
```

## NuGet Packages

| Purpose | Package |
| --- | --- |
| Client | `NSmithy.Client` |
| Server (ASP.NET Core) | `NSmithy.Server.AspNetCore` + `Microsoft.AspNetCore.App` |

## Modeling

Apply `@simpleRestJson` to the service and `@http` to each operation:

```smithy
$version: "2"

namespace example.hello

use alloy#simpleRestJson

@simpleRestJson
service HelloService {
    version: "2026-01-01"
    operations: [SayHello, CreateItem]
    errors: [ThrottlingError]
}

@http(method: "GET", uri: "/hello/{name}")
@readonly
operation SayHello {
    input := {
        @required @httpLabel
        name: String

        @httpQuery("verbose")
        verbose: Boolean
    }
    output := {
        @required
        message: String

        @httpHeader("x-request-id")
        requestId: String
    }
    errors: [NotFound]
}

@http(method: "POST", uri: "/items")
operation CreateItem {
    input := {
        @required
        name: String
    }
    output := {
        @required
        id: String
    }
}

@error("client") @httpError(404)
structure NotFound { message: String }

@error("server") @httpError(429)
structure ThrottlingError { message: String }
```

Key HTTP binding traits:

| Trait | Binds member to |
| --- | --- |
| `@httpLabel` | URI path segment |
| `@httpQuery("key")` | query string parameter |
| `@httpHeader("name")` | request or response header |
| `@httpPayload` | raw request/response body |

Members without an explicit binding go into the JSON body.

## Server

```csharp
using Example.Hello;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHelloServiceHandler<HelloHandler>();

var app = builder.Build();
app.MapHelloServiceHttp();
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input, CancellationToken ct = default)
    {
        if (input.Name == "nobody")
            throw new NotFoundException("not found");

        return Task.FromResult(new SayHelloOutput(
            Message: $"Hello, {input.Name}!",
            RequestId: Guid.NewGuid().ToString()
        ));
    }

    public Task<CreateItemOutput> CreateItemAsync(
        CreateItemInput input, CancellationToken ct = default) =>
        Task.FromResult(new CreateItemOutput(Guid.NewGuid().ToString()));
}
```

Throwing a generated error type from a handler method causes the adapter to
serialize it with the correct HTTP status code and JSON body.

## Client

```csharp
using Example.Hello;
using NSmithy.Client;

var client = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri("http://localhost:5000") }
);

var response = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(response.Message);    // Hello, world!
Console.WriteLine(response.RequestId); // from x-request-id header
```

`@httpHeader` members are deserialized from the response header automatically.

## Related

- [Multi-Protocol](/smithy-dotnet/guides/multi-protocol/) — serve the same handler over HTTP and gRPC simultaneously
- [Protocol Status](/smithy-dotnet/protocols/) — coverage overview
- [Conformance Tests](/smithy-dotnet/protocols/conformance/) — how correctness is verified
