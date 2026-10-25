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

NSmithy provides modeled endpoint rules, the AWS SDKs' default credential
chain and region resolution, presigning, and golden coverage against AWS's
published S3 signing vectors. SigV4a and the full production hardening of the
official AWS SDK are still outside this preview.
:::

Add `NSmithy.Aws` and configure `AwsSigV4AuthScheme`. For a service with
modeled [endpoint rules](/smithy-dotnet/guides/client-configuration/endpoint-rules/),
the resolved endpoint names the signing name and region, so the scheme needs
only credentials:

```csharp
using NSmithy.Aws;

var credentials = new DefaultAwsCredentialsProvider();

using var lambda = new LambdaClient(new LambdaClientConfig
{
    AuthSchemes = { new AwsSigV4AuthScheme(credentials) },
});
```

`Region` defaults to `AWS_REGION`, then `AWS_DEFAULT_REGION`, then the `region`
of the selected profile in `~/.aws/config` (`AwsRegion.Resolve()`). Set it to
use another region. Resolution reads no network, so on EC2 set `AWS_REGION`;
Lambda, ECS, and App Runner set it for you.

For a model without endpoint rules, pass the signing name and region, and
resolve the endpoint with `AwsRegionalEndpointResolver`:

```csharp
var region = "us-east-1";

using var lambda = new LambdaClient(new LambdaClientConfig
{
    EndpointResolver = new AwsRegionalEndpointResolver("lambda", region),
    AuthSchemes = { new AwsSigV4AuthScheme("lambda", region, credentials) },
});
```

The signer double-encodes and normalizes the canonical request path as SigV4
specifies. For S3, it signs the path as sent, as S3's endpoint rules and the
`s3` signing name require.

Available credential providers:

| Provider | Source |
| --- | --- |
| `DefaultAwsCredentialsProvider` | The sources below, in this order: environment, web identity, shared profile, container, instance metadata |
| `EnvironmentAwsCredentialsProvider` | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, optional `AWS_SESSION_TOKEN` |
| `WebIdentityAwsCredentialsProvider` | `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN` (EKS service accounts, GitHub Actions OIDC), exchanged with STS |
| `ProfileAwsCredentialsProvider` | A shared profile: an assumed role (`source_profile`, `credential_source`, or `web_identity_token_file`), IAM Identity Center, `credential_process`, or static keys |
| `ContainerAwsCredentialsProvider` | The container credentials endpoint of ECS, Fargate, App Runner, CodeBuild, and EKS Pod Identity |
| `InstanceMetadataAwsCredentialsProvider` | EC2 role credentials over IMDSv2 (optional IMDSv1 fallback) |
| `AssumeRoleAwsCredentialsProvider` | An IAM role assumed with credentials from another provider |
| `ProcessAwsCredentialsProvider` | The JSON output of a credential process command |
| `SsoAwsCredentialsProvider` | Explicit IAM Identity Center account/role using the AWS CLI token cache |
| `StaticAwsCredentialsProvider` | Explicit `AwsCredentials` instance |

The default chain skips sources that are not configured. A source that is
configured but fails, such as a profile whose role cannot be assumed, stops the
chain with its error. Profiles with `mfa_serial` are not supported, since they
need an interactive token code.

For presigned requests, construct an `AwsSigV4Presigner`, serialize the generated
operation request, and call `PresignAsync`. Durations are limited to AWS's range
of one second through seven days.

See [AWS Protocols](/smithy-dotnet/protocols/aws-overview/) for the supported AWS
runtime features and the remaining gaps. NSmithy also provides standard retries
and generated paginators for modeled `@paginated` operations.
