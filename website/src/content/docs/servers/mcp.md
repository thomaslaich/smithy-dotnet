---
title: MCP
description: Expose generated Smithy services through Model Context Protocol without reflection or transport coupling.
---

`NSmithy.Server.Mcp` exposes generated services through the
[Model Context Protocol](https://modelcontextprotocol.io/). It currently maps
unary operations to MCP tools. Support for prompts modeled with
`smithy.ai#prompts` is planned next.

| MCP capability | NSmithy support |
| --- | --- |
| Tools | Generated from unary operations |
| Prompts | Planned from `smithy.ai#prompts` |
| Resources | Not currently generated |

The package integrates with the official
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk). It does not
select a transport: the application can use stdio, HTTP, or another transport
provided by that SDK.

## Install the Package

```xml
<PackageReference Include="NSmithy.Server.Mcp" Version="NSMITHY_VERSION" />
```

The package brings in the MCP server hosting APIs and the NSmithy JSON and
server runtimes.

## Tools

The tools adapter uses generated JSON Schema 2020-12 documents, JSON codecs,
constraint validation, and typed handlers shared with the other NSmithy server
surfaces.

### Register Generated Operations

Register the generated handler as usual, then give its generated operation
catalog to `WithSmithyTools`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Server.Mcp;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWeatherServiceHandler<WeatherHandler>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithSmithyTools<IWeatherServiceHandler>(handler =>
        handler.CreateWeatherServiceOperationCatalog()
    );

await builder.Build().RunAsync();
```

The dependency-injection overload resolves the generated aggregate handler when
the MCP server is configured. An existing catalog can also be registered
directly:

```csharp
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithSmithyTools(handler.CreateWeatherServiceOperationCatalog());
```

### Operation Mapping

Each supported operation becomes one MCP tool:

| Smithy | MCP |
| --- | --- |
| Operation shape name | Tool name |
| `@documentation` on the operation | Tool description |
| Input structure and member constraints | JSON Schema 2020-12 input schema |
| Output shape | JSON Schema 2020-12 output schema and structured content |
| `@readonly` | `readOnlyHint` and a non-destructive hint |
| `@idempotent` | `idempotentHint` |
| `@jsonName` | JSON property name |

Arguments are deserialized by NSmithy's strict JSON codec and validated before
the handler runs. Missing required members, constraint violations, and modeled
errors are returned as MCP tool errors. Successful values are returned both as
structured content and as JSON text for clients that only consume text content.

JSON Schema generation is part of the C# code generator, not the MCP adapter.
Each generated unary server operation binding carries its input and output
documents in `JsonSchemas`. The shared `OperationSchema` and client-only output
do not carry this tool metadata. The schemas describe NSmithy's canonical JSON
document representation; they do not replace protocol-specific descriptions
such as OpenAPI.

Streaming operations are omitted because an MCP tool call has one JSON argument
object and one result.

The [restJson1 Weather example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/restjson1)
runs the same generated service and handler as either an ASP.NET Core server or
an MCP stdio server.

## Resources

Smithy resource shapes model API lifecycle and identifiers; they are not the
same concept as MCP resources. NSmithy does not currently infer MCP resources
from them, and the Smithy AI traits do not define an MCP-resource mapping.
