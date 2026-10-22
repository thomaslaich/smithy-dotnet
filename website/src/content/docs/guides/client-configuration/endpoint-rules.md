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

`Endpoint` falls back to `HttpClient.BaseAddress` when unset. A configured URL goes
through rules that model `SDK::Endpoint`. For S3, for example,
they still apply path-style addressing and reject a custom endpoint combined
with FIPS.

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

## Custom resolvers

A custom resolver works for services with or without modeled endpoint rules. It
replaces generated resolution and does not require a configured URL:

```csharp
using var client = new InventoryClient(new InventoryClientConfig
{
    EndpointResolver = new DirectoryResolver(directory),
});
```

The resolver receives:

| Input | Use |
| --- | --- |
| `ServiceId`, `OperationId` | Select a service or operation-specific destination. |
| `ConfiguredEndpoint` | Honor an optional configured override, if your resolver supports it. |
| `Input` | Read routing information from the actual generated request object. |
| `Operation` | Inspect modeled traits and input/output schemas; supplied by generated clients. |
| `CancellationToken` | Cancel discovery, including when the operation deadline expires. |

`Operation` can be null for manually constructed runtime bindings. For known input
types, cast `Input`; generic integrations can use schema visitors without reflection.
These inputs are not the generated rules' bound parameter bag. There is no arbitrary
per-call routing context API yet; use modeled input or injected dependencies.

Resolution happens before interceptor lifecycle hooks and serialization. It runs once
per invocation; retries reuse the endpoint. Shared resolvers must support concurrent
calls. A resolver can expose `StaticEndpoint` to skip per-call resolution only when
its complete result is constant.

### Example: an internal inventory service

This example uses `alloy#simpleRestJson` with no AWS endpoint functions or built-ins.
Its rules accept an explicit `SDK::Endpoint` override; otherwise `Environment` selects
`https://inventory.prod.example.test` or `https://inventory.test.example.test`.
Unsupported environments produce an error. The complete model and executable examples
are in `tests/NSmithy.Tests/model/inventory-endpoints.smithy` and
`tests/NSmithy.Tests/Client/InventoryEndpointExampleTests.cs`.

```csharp
// Generated rules choose the production URL.
using var production = new InventoryClient(new InventoryClientConfig
{
    Environment = "prod",
});

// The same rules accept a local URL.
using var local = new InventoryClient(new InventoryClientConfig
{
    Endpoint = new Uri("http://localhost:5000"),
});
```

An application can instead supply a directory shared across service clients:

```csharp
public sealed class DirectoryResolver(ServiceDirectory directory) : IEndpointResolver
{
    public ValueTask<SmithyEndpoint> ResolveEndpointAsync(
        SmithyEndpointParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new SmithyEndpoint(directory.GetEndpoint(parameters.ServiceId)));
    }
}
```

Here `ServiceDirectory` is an application-owned service mapping Smithy service IDs to
URLs. An implementation that performs network I/O can await it and pass cancellation.
It owns caching and refresh policy. To use DI:

```csharp
services.AddSingleton(directory);
services.AddSingleton<DirectoryResolver>();
services.AddInventoryClient((provider, config) =>
{
    config.EndpointResolver = provider.GetRequiredService<DirectoryResolver>();
});
```

Enable [generated DI registration](/smithy-dotnet/guides/client-configuration/dependency-injection/)
for this overload. Supplying `EndpointResolver` replaces the Inventory rules.

### Wrapping a generated resolver

A decorator may call a generated resolver and return its endpoint or a record copy:

```csharp
var endpoint = await inner.ResolveEndpointAsync(parameters, cancellationToken);
return endpoint with { Headers = updatedHeaders };
```

Preserve the result's metadata. In particular, `RequestPathAdapter` carries optional
service-specific path adjustment, such as S3 bucket addressing. It runs before URL
joining, signing, and retries. A record copy preserves it automatically. An ordinary
custom endpoint has no adapter and retains the modeled HTTP path. If you change the
destination, also consider whether its authentication properties need to change.

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

Every resolved AWS endpoint names its SigV4 signing scope: either the one the
rules set, or the service's `@sigv4` signing name and the configured `Region`.
That is why `AwsSigV4AuthScheme` needs only credentials here.
