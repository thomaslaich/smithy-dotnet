---
title: Quick Start
description: Get up and running with NSmithy.
---

This guide assumes the Smithy CLI and JDK are already available in your build
environment. If not, see [Environment Setup](/smithy-dotnet/getting-started/environment/) first.

## Configure The Project

Add the following to your `.csproj`. The codegen MSBuild targets are pulled in
automatically via `NSmithy.Server.AspNetCore` and `NSmithy.Client` — no
separate `NSmithy.MSBuild` reference is needed.

```xml
<ItemGroup>
  <!-- client -->
  <PackageReference Include="NSmithy.Client" Version="0.1.0-preview.11" />

  <!-- server (ASP.NET Core) -->
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.11" />
</ItemGroup>
```

Remove the server lines if you only need a generated client.

The `smithy-build.json` next to the `.csproj` is picked up automatically.
See the [MSBuild reference](/smithy-dotnet/reference/msbuild/) for the full property list.

## Add A Model

Add a `smithy-build.json` next to your `.csproj`:

```json
{
  "version": "1.0",
  "sources": ["model"],
  "maven": {
    "dependencies": [
      "com.disneystreaming.alloy:alloy-core:0.3.38",
      "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.1.0-preview.11"
    ]
  },
  "plugins": {
    "csharp-codegen": {
      "service": "example.hello#HelloService",
      "baseNamespace": ""
    }
  }
}
```

Add a model file at `model/hello.smithy`:

```smithy
$version: "2"

namespace example.hello

use alloy#simpleRestJson

@simpleRestJson
service HelloService {
    version: "2024-01-01"
    operations: [SayHello]
}

@http(method: "GET", uri: "/hello/{name}")
operation SayHello {
    input := {
        @required
        @httpLabel
        name: String
    }

    output := {
        @required
        message: String
    }
}
```

## Use The Generated Client

Run `dotnet build`. Generated files appear under `obj/<configuration>/<tfm>/Smithy/`.

```csharp
using Example.Hello;
using NSmithy.Client;

var client = new HelloServiceClient(
    new HttpClient(),
    new SmithyClientOptions { Endpoint = new Uri("http://localhost:8082") }
);

var output = await client.SayHelloAsync(new SayHelloInput("world"));
Console.WriteLine(output.Message);
```

## Use The Generated Server

For `alloy#simpleRestJson`, implement the generated handler interface and map
the routes:

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
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(new SayHelloOutput($"hello, {input.Name}"));
    }
}
```

## Next Steps

- [simple-rest-json example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/simple-rest-json) — a full working example with pixi environment
- [MSBuild Reference](/smithy-dotnet/reference/msbuild/) — all MSBuild properties and items for `NSmithy.MSBuild`
- [Multi-Protocol](/smithy-dotnet/guides/multi-protocol/) — expose one service over both HTTP and gRPC
- [Protocol Status](/smithy-dotnet/protocols/) — what protocols are supported and at what stage
