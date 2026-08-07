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
auth schemes modeled by the service and prepares one signer per configured
scheme. On each call it selects the first of the *operation's* effective
modeled schemes (a per-operation `@auth` trait overrides the service default)
for which you supplied configuration; operations modeled as anonymous send no
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
AWS SigV4 support is early preview. It is useful for narrow smoke tests and
LocalStack-style examples, but it is not a replacement for the AWS SDK for .NET.

Callers must provide the endpoint, signing service name, region, and credentials.
NSmithy does not yet provide AWS SDK-style endpoint resolution, profile/SSO/IMDS
credential chains, retries, paginators, presigning, or golden-vector coverage
against AWS's SigV4 test suite.
:::

Add `NSmithy.Aws` and configure `AwsSigV4AuthScheme`:

```csharp
using NSmithy.Aws;

var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
var endpoint = new Uri($"https://lambda.{region}.amazonaws.com");
var credentials = new EnvironmentAwsCredentialsProvider();

using var lambda = new LambdaClient(
    endpoint,
    new()
    {
        AuthSchemes = { new AwsSigV4AuthScheme("lambda", region, credentials) },
    });
```

Available credential providers:

| Provider | Source |
| --- | --- |
| `StaticAwsCredentialsProvider` | Explicit `AwsCredentials` instance |
| `EnvironmentAwsCredentialsProvider` | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, optional `AWS_SESSION_TOKEN` |

For production AWS integrations, prefer the official
[AWS SDK for .NET](https://github.com/aws/aws-sdk-net) until NSmithy's AWS auth,
endpoint resolution, retries, and pagination support mature.
