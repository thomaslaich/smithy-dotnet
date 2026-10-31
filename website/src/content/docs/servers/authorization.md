---
title: Authorization
description: Require authorization for the operations whose model requires authentication.
---

Smithy models auth per operation. A service applies auth scheme traits such as
`@httpBearerAuth`, and each operation can narrow them with `@auth`, drop them
with `@auth([])`, or make them optional with `@optionalAuth`:

```smithy
@httpBearerAuth
service PetService {
    operations: [AdoptPet, ListPets, GetHealth]
}

operation AdoptPet {}       // requires a bearer token

@optionalAuth
operation ListPets {}       // works with or without one

@auth([])
operation GetHealth {}      // takes no auth
```

How credentials are checked is up to the application: register an ASP.NET Core
authentication handler (JWT bearer, an API key handler, ...) as usual.
`WithModeledAuthorization()` then decides from the model which operations need
it:

```csharp title="Program.cs"
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();
builder.Services.AddPetServiceHandler<PetHandler>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapPetService().WithModeledAuthorization();
```

| Operation | Endpoint metadata |
| --- | --- |
| an auth scheme applies | `[Authorize]` with the given policies, or the default policy |
| `@optionalAuth`, `@auth([])`, or no auth scheme on the service | `[AllowAnonymous]` |

`[AllowAnonymous]` overrides authorization required by an enclosing group or a
fallback policy, so operations the model marks public stay reachable. With
`@optionalAuth`, a caller who sends valid credentials is still authenticated;
invalid credentials make the caller anonymous instead of failing the call.

Pass policy names to authorize protected operations with them instead of the
default policy:

```csharp
app.MapPetService().WithModeledAuthorization("PetOwners");
```

Without `WithModeledAuthorization()`, the model's auth has no effect on the
server and the application protects endpoints as it would any other.

## Protocols with a single endpoint

rpcv2Cbor and the AWS JSON protocols map one endpoint for all operations and
read the operation from the request. When those operations are all protected,
the endpoint requires authorization like any other. When some are public, the
endpoint allows anonymous calls and authorizes a protected operation once it is
resolved, after the request body is read.

## Custom conventions

`Map{Service}` returns a `SmithyServiceEndpointConventionBuilder`, so ASP.NET
Core's endpoint conventions apply to every operation of the service:

```csharp
app.MapPetService().RequireRateLimiting("pets");
```

Each endpoint carries `SmithyEndpointMetadata` with the operations it serves.
Each operation's `AuthSchemes` lists its effective scheme IDs in priority
order, so a convention can pick authentication schemes per operation:

```csharp
var bearerAuth = ShapeId.Parse("smithy.api#httpBearerAuth");
app.MapPetService().Add(endpoint =>
{
    var smithy = endpoint.Metadata.OfType<SmithyEndpointMetadata>().Single();
    if (smithy.Operations.Any(o => o.AuthSchemes.Contains(bearerAuth)))
    {
        endpoint.Metadata.Add(new AuthorizeAttribute { AuthenticationSchemes = "Bearer" });
    }
});
```
