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

The rules are compiled into C# at build time. Nothing is parsed or interpreted
at runtime.

## Parameters

Rule parameters that client configuration supplies become properties of
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
| `EndpointResolver` set | The configured resolver. The rules are not used. |
| `Endpoint` set | The rules, with `Endpoint` as the custom endpoint. |
| Neither | The rules, from the configured parameters. |

A configured `Endpoint` still goes through the rules. For S3, for example,
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
