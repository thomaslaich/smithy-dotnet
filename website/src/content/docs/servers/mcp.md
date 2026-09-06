---
title: MCP
description: Expose generated Smithy services through Model Context Protocol without reflection or transport coupling.
---

`NSmithy.Server.Mcp` exposes generated services through the
[Model Context Protocol](https://modelcontextprotocol.io/). It maps unary
operations to MCP tools and `smithy.ai#prompts` traits to MCP prompts.

| MCP capability | NSmithy support |
| --- | --- |
| Tools | Generated from unary operations |
| Prompts | Generated from `smithy.ai#prompts` on services and operations |
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

## Register a Generated Service

Register the generated handler as usual, then select the service by its generated
schema. This exposes its tools and prompts together:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Server.Mcp;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWeatherServiceHandler<WeatherHandler>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithSmithyService(WeatherSchema.Schema);

await builder.Build().RunAsync();
```

`AddWeatherServiceHandler` also registers the generated service definition.
That definition resolves each operation through its per-operation handler
interface, so MCP does not require an aggregate handler. For separately
implemented operation handlers, register the service definition explicitly:

```csharp
builder.Services.AddWeatherService();
builder.Services.AddSingleton<IGetCityHandler, GetCityHandler>();
builder.Services.AddSingleton<IGetForecastHandler, GetForecastHandler>();
// Register the remaining operation handlers exposed by the service.
```

`WithSmithyService` is explicit: registering a service or its handlers in DI
does not expose it to MCP until its schema is selected.

## Tools

The tools adapter uses generated JSON Schema 2020-12 documents, JSON codecs,
constraint validation, and typed handlers shared with the other NSmithy server
surfaces.

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

An application that builds its own `ServiceOperationCatalog` can expose only
that catalog with `WithSmithyTools(catalog)`. This is the low-level escape hatch;
generated services normally use `WithSmithyService`.

## Prompts

Add `smithy.ai#prompts` to a service or operation:

```smithy
use smithy.ai#prompts

@prompts({
    city_weather_brief: {
        description: "Create a weather brief for a city"
        template: "Use GetCity and GetForecast for city ID {{cityId}}, then summarize the result."
        arguments: CityWeatherBriefArguments
        preferWhen: "The user asks for a combined city and weather overview"
    }
})
service Weather {
    version: "2006-03-01"
    operations: [GetCity, GetForecast]
}

structure CityWeatherBriefArguments {
    /// City ID accepted by the Weather service.
    @required
    cityId: String
}
```

The generated MCP prompt has the name `city_weather_brief`, its modeled
description, and a required string argument named `cityId`. Resolving it with
`cityId = "SEA"` returns this user message:

```text
Use GetCity and GetForecast for city ID SEA, then summarize the result.

Tool preference: The user asks for a combined city and weather overview
```

A template is text with `{{argumentName}}` placeholders. NSmithy substitutes
the MCP prompt arguments and returns the rendered text to the client. It does
not call either operation itself. The instructions establish the relationship:
the model sees the rendered prompt and the available MCP tools, then decides
whether and how to invoke `GetCity` and `GetForecast`. One prompt can therefore
guide zero, one, or several tool calls.

Prompt names must be unique within the generated service when compared without
regard to case. Required and optional status plus argument documentation come
from the referenced Smithy structure. Unknown arguments, non-string values, and
missing required arguments produce MCP `InvalidParams` errors.

Handwritten prompt definitions can be registered directly with
`WithSmithyPrompts(definitions)`, independently of generated service tools.

The [restJson1 Weather example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/restjson1)
runs the same generated service and handler as either an ASP.NET Core server or
an MCP stdio server, and includes both single-tool and multi-tool prompts.

## Resources

Smithy resource shapes model API lifecycle and identifiers; they are not the
same concept as MCP resources. NSmithy does not currently infer MCP resources
from them, and the Smithy AI traits do not define an MCP-resource mapping.
