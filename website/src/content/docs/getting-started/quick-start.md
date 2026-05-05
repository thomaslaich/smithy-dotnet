---
title: Quick Start
description: Get up and running with NSmithy.
---

This guide assumes the Smithy CLI and JDK are already available in your build
environment. If not, see [Environment Setup](/smithy-dotnet/getting-started/environment/) first.

## Configure The Project

Add the following to your `.csproj`. The single `ItemGroup` covers both clients
and servers — remove the server packages if you only need generated clients.

```xml
<ItemGroup>
  <!-- code generation (build-time only) -->
  <PackageReference Include="NSmithy.MSBuild" Version="0.1.0-preview.7" PrivateAssets="all" />

  <!-- core runtime (always required) -->
  <PackageReference Include="NSmithy.Core" Version="0.1.0-preview.7" />
  <PackageReference Include="NSmithy.Http" Version="0.1.0-preview.7" />
  <PackageReference Include="NSmithy.Client" Version="0.1.0-preview.7" />
  <PackageReference Include="NSmithy.Codecs.Json" Version="0.1.0-preview.7" />

  <!-- server runtime (only needed for generated ASP.NET Core servers) -->
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
  <PackageReference Include="NSmithy.Server" Version="0.1.0-preview.7" />
  <PackageReference Include="NSmithy.Server.AspNetCore" Version="0.1.0-preview.7" />
</ItemGroup>
```

NSmithy.MSBuild picks up the `smithy-build.json` next to the `.csproj`
automatically — no additional MSBuild properties are needed for basic use.
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
      "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.1.0-preview.7"
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

- [Minimal pixi example](https://github.com/thomaslaich/smithy-dotnet-minimal-pixi) — a standalone repo with everything wired up
- [MSBuild Reference](/smithy-dotnet/reference/msbuild/) — all MSBuild properties and items for `NSmithy.MSBuild`
- [Multi-Protocol](/smithy-dotnet/guides/multi-protocol/) — expose one service over both HTTP and gRPC
- [Protocol Status](/smithy-dotnet/protocols/) — what protocols are supported and at what stage
