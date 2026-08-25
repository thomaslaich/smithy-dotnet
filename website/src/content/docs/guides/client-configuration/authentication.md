---
title: Authentication
description: Configure generated clients with modeled auth schemes, including early-preview AWS SigV4 signing.
---

Smithy models describe which auth schemes a service supports, but generated
clients still need runtime auth configuration. Add auth schemes through
`{Service}ClientConfig.AuthSchemes`:

```csharp
using NSmithy.Client;

var client = new WeatherClient(
    new Uri("https://api.example.com"),
    new()
    {
        AuthSchemes = { new HttpBearerAuthScheme(token) },
    });
```

At construction time, NSmithy validates your configured schemes against the
auth schemes modeled by the service. On each call it selects the first of the
*operation's* effective
modeled schemes (a per-operation `@auth` trait overrides the service default)
for which you supplied configuration, resolves that scheme's identity, and
signs each attempt after user request interceptors have run. Operations modeled as anonymous send no
credentials, and an [endpoint resolver](/smithy-dotnet/guides/client-configuration/)
can narrow the candidate schemes per endpoint. If `AuthSchemes` is empty,
requests are sent anonymously.

The generated client sends credentials. Server-side authorization is still
application code in this preview: generated ASP.NET Core handlers can read
modeled auth headers or the ASP.NET Core request context, but NSmithy does not
yet generate policy enforcement from auth traits.

## HTTP Auth Schemes

`NSmithy.Client` includes simple HTTP auth schemes:

| Scheme | Modeled trait | Runtime type |
| --- | --- | --- |
| Bearer token | `smithy.api#httpBearerAuth` | `HttpBearerAuthScheme` |
| Basic auth | `smithy.api#httpBasicAuth` | `HttpBasicAuthScheme` |
| API key | `smithy.api#httpApiKeyAuth` | `HttpApiKeyAuthScheme` |

Example:

```csharp
using NSmithy.Client;

var client = new WeatherClient(
    new Uri("https://api.example.com"),
    new()
    {
        AuthSchemes = { new HttpApiKeyAuthScheme("X-Api-Key", apiKey) },
    });
```

The `simplerestjson` example includes an API-key-protected operation that
validates the header sent by `HttpApiKeyAuthScheme`.

## AWS SigV4

:::caution[Early preview]
For most applications that call AWS from .NET, use the official AWS SDK for
.NET. NSmithy's AWS integration support is intended for focused integrations,
emulators, and protocol validation, not as a complete SDK replacement.

NSmithy now provides standard regional endpoint resolution, environment and
shared-profile credentials (including cached IAM Identity Center sessions),
IMDSv2 role credentials, presigning, and golden coverage against AWS's published
S3 signing vectors. Modeled per-service endpoint rule sets, assume-role and web
identity providers, ECS container credentials, SigV4a, and the full production
hardening of the official AWS SDK are still outside this preview.
:::

Add `NSmithy.Aws` and configure `AwsSigV4AuthScheme`:

```csharp
using NSmithy.Aws;

var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
var endpoint = new AwsRegionalEndpointResolver("lambda", region);
var credentials = new DefaultAwsCredentialsProvider();

using var lambda = new LambdaClient(
    new HttpClient(),
    new LambdaClientConfig
    {
        EndpointResolver = endpoint,
        AuthSchemes = { new AwsSigV4AuthScheme("lambda", region, credentials) },
    });
```

Available credential providers:

| Provider | Source |
| --- | --- |
| `StaticAwsCredentialsProvider` | Explicit `AwsCredentials` instance |
| `EnvironmentAwsCredentialsProvider` | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, optional `AWS_SESSION_TOKEN` |
| `ProfileAwsCredentialsProvider` | Static shared profiles or cached IAM Identity Center/SSO sessions |
| `SsoAwsCredentialsProvider` | Explicit IAM Identity Center account/role using the AWS CLI token cache |
| `InstanceMetadataAwsCredentialsProvider` | EC2 role credentials over IMDSv2 (optional IMDSv1 fallback) |
| `DefaultAwsCredentialsProvider` | Environment → shared profile/SSO → IMDS |

For presigned requests, construct an `AwsSigV4Presigner`, serialize the generated
operation request, and call `PresignAsync`. Durations are limited to AWS's range
of one second through seven days.

See [AWS Protocols](/smithy-dotnet/protocols/aws-overview/) for the supported AWS
runtime features and the remaining gaps. NSmithy also provides standard retries
and generated paginators for modeled `@paginated` operations.
