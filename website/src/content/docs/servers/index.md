---
title: Servers
description: Implement generated handlers and expose them through ASP.NET Core.
---

NSmithy generates handler interfaces whose methods accept modeled inputs and
return modeled outputs. You implement the operations; the runtime handles
serialization, dispatch, and modeled errors.

## Implement an operation

For the [Book model](/smithy-dotnet/concepts/modeling/), implement the generated
service interface:

```csharp
internal sealed class LibraryHandler : ILibraryServiceHandler
{
    public Task<GetBookOutput> GetBookAsync(
        GetBookInput input, CancellationToken ct = default)
    {
        if (input.Id != "1")
            throw new BookNotFound("Book not found.");

        return Task.FromResult(new GetBookOutput(input.Id, "The Odyssey"));
    }
}
```

Return an output for success; throw a generated error for a modeled failure.
The selected protocol determines the response encoding and status.

## Register and map

```csharp
using Example.Library;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLibraryServiceHandler<LibraryHandler>();

var app = builder.Build();
app.MapLibraryService();
app.Run();
```

`Add{Service}Handler<THandler>` registers the implementation, service metadata,
and default server runtime. `Map{Service}` maps the first declared supported
server protocol. To select several, see
[Hosting multiple protocols](/smithy-dotnet/servers/hosting/).

## Separate operation handlers

The service interface inherits one interface per operation:

```csharp
public interface IGetBookHandler
{
    Task<GetBookOutput> GetBookAsync(GetBookInput input, CancellationToken ct = default);
}

public interface ILibraryServiceHandler : IGetBookHandler { }
```

Endpoints resolve the per-operation interfaces. To split a service across
implementations, register each one instead of using the aggregate helper:

```csharp
builder.Services.AddLibraryService();
builder.Services.AddSmithyServer(); // using NSmithy.Server.AspNetCore;
builder.Services.AddScoped<IGetBookHandler, GetBookHandler>();
```

Register a handler for every mapped operation. `GetBookHandler` implements
`IGetBookHandler` with the same method shown above.

## Request validation

The runtime validates supported constraints before invoking the handler.
See [Validation](/smithy-dotnet/servers/validation/) for checked traits,
error responses, and streaming limits.

See [Protocol status](/smithy-dotnet/protocols/status/) for ASP.NET Core protocol
support, or [MCP](/smithy-dotnet/servers/mcp/) to expose handlers as tools and prompts.
