---
title: How NSmithy works
description: What generated code does and what the runtime does, and how a client call and a server request flow through them.
---

Generated code describes the service; runtime libraries act on it. The
generator emits model types, a schema for every shape, and the client and server
surface. Wire formats, routing, retries, validation, and error handling live in
hand-written NuGet packages that read those schemas.

| Part | Generated or runtime | Owns |
| --- | --- | --- |
| Model types | Generated | The service's data as C# records, unions, enums, and exceptions |
| Schemas | Generated | Each shape's structure and traits, and serializing its values |
| Client and server surface | Generated | Typed methods, handler interfaces, and the service's table of operations |
| Codecs | Runtime | One wire format each: JSON, CBOR, XML, Protobuf |
| Protocols | Runtime | Encoding, routing, and error representation of one Smithy protocol |
| Client runtime | Runtime | A call's lifecycle: endpoint, auth, interceptors, retries, telemetry |
| Server runtime and host | Runtime | Dispatching a request to its handler on ASP.NET Core |

## Schemas

A schema such as `GetCityInputSchema` describes its shape's members and traits,
and serializes values of the shape by handing each member to whatever consumes
it. A JSON codec writes the member as JSON, the validator checks its
constraints, and an HTTP binding puts it in a path label or header. Every
consumer goes through the same generated code, so a new wire format or protocol
needs no new generated code, and nothing reflects over model types.
[Code generation](/smithy-dotnet/concepts/code-generation/#runtime-schemas)
shows what a schema carries.

## Protocols

A protocol is a runtime object, such as `RestJson1Protocol`. It binds an
operation's schema to HTTP requests and responses: how input is encoded, which
route an operation answers on, and how a modeled error is represented and
recognized. Generated code never branches on a protocol. A client uses one
protocol at a time and can switch it in configuration; a server can serve the
same handlers over several at once, as
[Hosting multiple protocols](/smithy-dotnet/servers/hosting/) shows.

## A client call

`client.GetCityAsync(input)` passes the input and the operation's binding to
the client runtime, which runs every call the same way:

```text
resolve the endpoint and select an auth scheme
  -> run interceptors
  -> protocol serializes the request
  -> sign and send the attempt (retried by the retry strategy)
  -> protocol deserializes the output, or throws the modeled error
```

Auth, [retries](/smithy-dotnet/guides/client-configuration/retry/),
[interceptors](/smithy-dotnet/guides/client-configuration/interceptors/), and
[telemetry](/smithy-dotnet/guides/client-configuration/observability/) are
therefore client configuration, not generated code, and apply to every
operation and protocol alike. Paginators and waiters call the client's own
operation methods, so each page or poll is a normal call.

## A server request

`MapWeatherService` hands the service's operations and the selected protocols
to the ASP.NET Core host. Each protocol reports its routes, and the host maps
them as endpoints. A request then flows:

```text
host converts the HttpContext into a Smithy request
  -> protocol deserializes the input
  -> validator checks the model's constraints
  -> handler runs: GetCityAsync(input, cancellationToken)
  -> protocol serializes the output, or the modeled error the handler threw
host writes the response
```

The handler sees only typed model values. [Validation](/smithy-dotnet/servers/validation/)
rejects a request that breaks the model before the handler runs, and the
[MCP](/smithy-dotnet/servers/mcp/) host serves the same operations from the same
table.

## Why it matters

Because behavior lives in the runtime, one model and one implementation serve
several protocols, fakes and MCP reuse the same operations, and NSmithy can be
extended with a new protocol or service generator from a package of its own; see
[Extending NSmithy](/smithy-dotnet/reference/extending/).
