---
title: Endpoint rules
description: Generated endpoint resolvers for services with modeled Smithy endpoint rules.
---

A service that declares `@smithy.rules#endpointRuleSet` or
`@smithy.rules#endpointBdd` gets a generated `{Service}EndpointResolver`. Its
client resolves endpoints from the rules, so it is configured with the rule
parameters instead of a URL:

```csharp
using NSmithy.Aws;

using var lambda = new LambdaClient(new LambdaClientConfig
{
    Region = "eu-central-1",
    UseFips = true,
    AuthSchemes = { new AwsSigV4AuthScheme(new DefaultAwsCredentialsProvider()) },
});
```

`Region` and `UseFips` are generated from the parameters Lambda's endpoint
rules declare. Another service's configuration has the properties its own rules
declare.

`LambdaEndpointResolver` implements `IEndpointResolver`. The client creates it
from its configuration unless `EndpointResolver` is set. The rules are compiled
into C# at build time. Nothing is parsed or interpreted at runtime.

## Parameters

The rules declare their parameters. Lambda's include:

```json
"parameters": {
  "Region": { "type": "string", "builtIn": "AWS::Region" },
  "UseFIPS": { "type": "boolean", "builtIn": "AWS::UseFIPS", "required": true, "default": false },
  "Endpoint": { "type": "string", "builtIn": "SDK::Endpoint" }
}
```

Parameters that client configuration supplies become properties of
`{Service}ClientConfig`:

| Parameter source | Configuration property |
| --- | --- |
| Built-in, such as `AWS::Region` | Named by the integration that provides it, such as `Region` |
| `@clientContextParams` entry | The parameter name in PascalCase |
| `SDK::Endpoint` | The existing `Endpoint` |

A property left unset takes the parameter's default from the rules. Parameters
bound by `@staticContextParams`, `@contextParam`, or `@operationContextParams`
come from each operation and its input.

## Precedence

| Configuration | Resolution |
| --- | --- |
| `EndpointResolver` set | The configured resolver. The generated resolver is not used. |
| `Endpoint` set and rules model `SDK::Endpoint` | The rules, with `Endpoint` as the custom endpoint. |
| `Endpoint` set and rules do not model `SDK::Endpoint` | The configured URL, bypassing rules. |
| Neither | The rules, from the configured parameters. |

`Endpoint` falls back to `HttpClient.BaseAddress`. Rules that model
`SDK::Endpoint` still apply to a configured URL. S3's, for example, apply
path-style addressing and reject a custom endpoint combined with FIPS.

## Results

A resolved endpoint carries the request URL, headers added to every request
sent to it, and the auth schemes it supports with their properties. The auth
schemes narrow [auth selection](/smithy-dotnet/guides/client-configuration/authentication/),
and the selected scheme's signer reads its properties, such as the SigV4
signing name and region.

When the rules end in an error, such as a missing region, the client throws
`SmithyEndpointResolutionException` with the rules' message. Rules that
depend only on client configuration are resolved once, when the client is
constructed. Rules that also bind operation input are resolved on each call,
before the request is serialized.

## Rules for your own services

Endpoint rules are not specific to AWS. This service picks its host from an
`Environment` client context parameter and accepts an explicit URL through
`SDK::Endpoint`:

```smithy
@clientContextParams(Environment: { type: "string" })
@endpointRuleSet({
    version: "1.0"
    parameters: {
        Environment: { type: "String", required: true, default: "test" }
        Endpoint: { type: "String", builtIn: "SDK::Endpoint" }
    }
    rules: [
        {
            conditions: [{ fn: "isSet", argv: [{ ref: "Endpoint" }] }]
            type: "endpoint"
            endpoint: { url: { ref: "Endpoint" } }
        }
        {
            conditions: [{ fn: "stringEquals", argv: [{ ref: "Environment" }, "prod"] }]
            type: "endpoint"
            endpoint: { url: "https://inventory.example.com" }
        }
        {
            conditions: [{ fn: "stringEquals", argv: [{ ref: "Environment" }, "test"] }]
            type: "endpoint"
            endpoint: { url: "https://inventory.test.example.com" }
        }
        { conditions: [], type: "error", error: "Environment must be prod or test" }
    ]
})
service Inventory { ... }
```

```csharp
using var production = new InventoryClient(new InventoryClientConfig { Environment = "prod" });
using var local = new InventoryClient(new InventoryClientConfig
{
    Endpoint = new Uri("http://localhost:5000"),
});
```

Rules that need functions or built-ins beyond the standard library need a
`CSharpIntegration` that maps them to C#, as `NSmithy.Aws` does for AWS. See
[Extending endpoint rules](/smithy-dotnet/reference/extending/#extending-endpoint-rules).

## Custom resolvers

Set `EndpointResolver` to replace generated resolution. This works whether or
not the service models endpoint rules, and the client then needs no `Endpoint`.
A resolver implements:

```csharp
public interface IEndpointResolver
{
    ValueTask<SmithyEndpoint> ResolveEndpointAsync(
        SmithyEndpointParameters parameters,
        CancellationToken cancellationToken = default);

    SmithyEndpoint? StaticEndpoint => null;
}
```

`SmithyEndpointParameters` describes one call:

| Member | Content |
| --- | --- |
| `ServiceId`, `OperationId` | The Smithy shape ids of the service and operation |
| `ConfiguredEndpoint` | The client's `Endpoint`, if set |
| `Input` | The operation input. Cast it to the generated input type to route on its members. |
| `Operation` | The operation's schema, with its traits and input and output schemas |

Anything else the resolver needs, such as a service registry, comes in through
its constructor.

The client resolves the endpoint once per call, before serializing the request
and before interceptors run. Retries reuse it. One resolver serves concurrent
calls, so it must be thread-safe. A resolver whose result never changes returns
it from `StaticEndpoint`, and the client then skips per-call resolution.

This resolver looks services up in a registry:

```csharp
public sealed class RegistryResolver(ServiceRegistry registry) : IEndpointResolver
{
    public async ValueTask<SmithyEndpoint> ResolveEndpointAsync(
        SmithyEndpointParameters parameters,
        CancellationToken cancellationToken = default) =>
        new SmithyEndpoint(await registry.LookupAsync(parameters.ServiceId, cancellationToken));
}
```

To take the resolver from dependency injection, use the
[registration overload](/smithy-dotnet/guides/client-configuration/dependency-injection/#resolving-configuration-dependencies)
that passes the service provider:

```csharp
services.AddSingleton<RegistryResolver>();
services.AddInventoryClient((provider, config) =>
{
    config.EndpointResolver = provider.GetRequiredService<RegistryResolver>();
});
```

### Wrapping a generated resolver

A generated resolver is an ordinary `IEndpointResolver`, so a decorator can call
it and adjust the result:

```csharp
public sealed class TracingHeaderResolver(IEndpointResolver inner) : IEndpointResolver
{
    public async ValueTask<SmithyEndpoint> ResolveEndpointAsync(
        SmithyEndpointParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await inner.ResolveEndpointAsync(parameters, cancellationToken);
        return endpoint with
        {
            Headers = new Dictionary<string, IReadOnlyList<string>>(
                endpoint.Headers ?? new Dictionary<string, IReadOnlyList<string>>())
            {
                ["x-trace-origin"] = ["inventory"],
            },
        };
    }
}

var config = new InventoryClientConfig { Environment = "prod" };
config.EndpointResolver = new TracingHeaderResolver(new InventoryEndpointResolver(config));
```

The record copy keeps the endpoint's auth schemes and their signing properties.
A decorator that changes the host should check that they still apply.

## AWS services

AWS rules use the `aws.partition`, `aws.parseArn`, and
`aws.isVirtualHostableS3Bucket` functions and `AWS::*` built-ins. Reference
`NSmithy.Aws`, which implements the functions and carries the partition data
of the Smithy version NSmithy builds with.

The AWS built-ins map to these properties. A client has only those its
service's rules declare.

| Built-in | Property |
| --- | --- |
| `AWS::Region` | `Region` |
| `AWS::UseFIPS` | `UseFips` |
| `AWS::UseDualStack` | `UseDualStack` |
| `AWS::Auth::AccountId` | `AccountId` |
| `AWS::Auth::AccountIdEndpointMode` | `AccountIdEndpointMode` |
| `AWS::Auth::CredentialScope` | `CredentialScope` |
| `AWS::S3::Accelerate` | `Accelerate` |
| `AWS::S3::DisableMultiRegionAccessPoints` | `DisableMultiRegionAccessPoints` |
| `AWS::S3::ForcePathStyle` | `ForcePathStyle` |
| `AWS::S3::UseArnRegion`, `AWS::S3Control::UseArnRegion` | `UseArnRegion` |
| `AWS::S3::UseGlobalEndpoint`, `AWS::STS::UseGlobalEndpoint` | `UseGlobalEndpoint` |

S3's rules put the bucket in the host, or in the endpoint's path for path-style
addressing, so generated S3 operations have no `/{Bucket}` path segment. A
custom S3 resolver adds the bucket to the endpoint itself.

Every resolved AWS endpoint names its SigV4 signing scope: either the one the
rules set, or the service's `@sigv4` signing name and the configured `Region`.
That is why `AwsSigV4AuthScheme` needs only credentials here.
