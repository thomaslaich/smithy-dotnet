---
title: simpleRestJson
description: Build a typed HTTP client and ASP.NET Core server with alloy#simpleRestJson.
---

`alloy#simpleRestJson` is the most complete protocol in the current NSmithy
preview. This guide walks through a service with two operations — one `GET` with
a path label and one `POST` with a JSON body — including error modeling and
response headers.

## Model

```smithy
$version: "2"

namespace example.hello

use alloy#simpleRestJson

@simpleRestJson
service HelloService {
    version: "2026-01-01"
    operations: [SayHello, Ping]
    errors: [ThrottlingError]
}

@http(method: "GET", uri: "/hello/{name}")
@readonly
operation SayHello {
    input := {
        @required @httpLabel
        name: String
    }
    output := {
        @required
        message: String

        @httpHeader("x-request-id")
        requestId: String
    }
    errors: [NotFound]
}

@http(method: "POST", uri: "/ping")
operation Ping {
    input := {
        @required
        name: String
    }
    output := {
        @required
        message: String
    }
}

@error("client")
@httpError(404)
structure NotFound {
    message: String
}

@error("server")
@httpError(429)
structure ThrottlingError {
    message: String
}
```

Key traits used here:

- `@httpLabel` — binds `name` to the `{name}` segment in the URI
- `@httpHeader` — binds `requestId` to the `x-request-id` response header
- `@error("client")` / `@error("server")` — marks error shapes; `@httpError` sets the status code

## Server

NSmithy generates one `IHelloServiceHandler` interface with a method for each
operation. You implement it once; the generated ASP.NET Core adapter handles
routing, deserialization, and error dispatch.

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
        SayHelloInput input,
        CancellationToken ct = default)
    {
        if (input.Name == "nobody")
            throw new NotFoundException("not found");

        return Task.FromResult(new SayHelloOutput(
            Message: $"Hello, {input.Name}!",
            RequestId: Guid.NewGuid().ToString()
        ));
    }

    public Task<PingOutput> PingAsync(
        PingInput input,
        CancellationToken ct = default) =>
        Task.FromResult(new PingOutput($"Pong, {input.Name}!"));
}
```

Throwing a generated error type from a handler method causes the generated
adapter to serialize it with the correct HTTP status code and JSON error body.

## Client

```csharp
using Example.Hello;
using NSmithy.Client;

var client = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri("http://localhost:5000") }
);

var hello = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(hello.Message);      // Hello, world!
Console.WriteLine(hello.RequestId);    // x-request-id header value

var ping = await client.PingAsync(new PingInput("world"));
Console.WriteLine(ping.Message);       // Pong, world!
```

The client deserializes `@httpHeader` members automatically — `RequestId` is
populated from the `x-request-id` response header, not the JSON body.

## Related

- [Multi-Protocol](/smithy-dotnet/guides/multi-protocol/) — serve the same handler over HTTP and gRPC simultaneously
- [Protocol Status](/smithy-dotnet/protocols/) — current coverage and conformance status
