# AWS Credentials and Region

How an AWS client finds its credentials and region when the application does
not set them.

## Overview

A client for an AWS service needs credentials to sign requests and a region to
resolve its endpoint. Where an application runs decides where both come from:
environment variables on a developer machine or in CI, a shared profile with
an assumed role or IAM Identity Center, a web identity token on EKS or GitHub
Actions, a container endpoint on ECS, Fargate, or EKS Pod Identity, and
instance metadata on EC2. `NSmithy.Aws` resolves both from those sources in
the order the AWS SDKs use, so the same code runs unchanged in each place:

```csharp
using var dynamo = new DynamoDbClient(new DynamoDbClientConfig
{
    AuthSchemes = { new AwsSigV4AuthScheme(new DefaultAwsCredentialsProvider()) },
});
```

`Region` is left unset; the client reads it from `AWS_REGION` or the profile.
An application that sets `Region` or passes another credentials provider
overrides the defaults.

## Goals

- `DefaultAwsCredentialsProvider` covers the credential sources of the AWS
  SDKs' default chain, in their order.
- Generated AWS clients default `Region` from the environment and shared
  config.
- Credentials that expire are refreshed before they do, once, shared across
  concurrent calls.
- Every source is testable without a network, files, or environment: paths,
  environment, HTTP, clock, and processes are injectable.
- No dependency beyond the BCL and the NSmithy runtime.

## Non-goals

- Interactive MFA. A profile with `mfa_serial` fails with a message naming it.
- Region from EC2 instance metadata. Region resolution is synchronous and
  reads no network; EC2 applications set `AWS_REGION`, which Lambda, ECS, and
  App Runner set for them.
- Refreshing IAM Identity Center tokens. The provider reads the token the AWS
  CLI caches, as it does today.

## Credential chain

`DefaultAwsCredentialsProvider` tries each source in order and uses the first
that is configured:

| Order | Source | Configured by |
| --- | --- | --- |
| 1 | Environment | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, optional `AWS_SESSION_TOKEN` |
| 2 | Web identity | `AWS_WEB_IDENTITY_TOKEN_FILE` and `AWS_ROLE_ARN`, optional `AWS_ROLE_SESSION_NAME` |
| 3 | Shared profile | The profile `AWS_PROFILE` names, or `default` (see [Profiles](#profiles)) |
| 4 | Container | `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI` or `AWS_CONTAINER_CREDENTIALS_FULL_URI` |
| 5 | Instance metadata | IMDSv2 on EC2, unless `AWS_EC2_METADATA_DISABLED` is `true` |

A source that is not configured throws `AwsCredentialsProviderException` with
`IsNotConfigured`, and the chain moves on. A source that is configured but
fails, such as a role it cannot assume, stops the chain with that error: a
misconfigured profile must not silently fall through to instance metadata.

The chain remembers the source that succeeded and asks only that source on
later calls, so refreshing credentials does not walk the chain again.

### Profiles

`ProfileAwsCredentialsProvider` reads the shared `config` and `credentials`
files (`AWS_CONFIG_FILE`, `AWS_SHARED_CREDENTIALS_FILE`) and resolves the
selected profile by the first rule that applies:

| Profile keys | Credentials |
| --- | --- |
| `role_arn` and `source_profile` | AssumeRole with the source profile's credentials |
| `role_arn` and `credential_source` | AssumeRole with credentials from `Environment`, `Ec2InstanceMetadata`, or `EcsContainer` |
| `role_arn` and `web_identity_token_file` | AssumeRoleWithWebIdentity |
| `sso_session` or `sso_start_url` | IAM Identity Center role credentials from the CLI's token cache |
| `credential_process` | The output of the command |
| `aws_access_key_id` and `aws_secret_access_key` | Static credentials |

`role_session_name`, `external_id`, and `duration_seconds` apply to the role
rules. A `source_profile` may itself assume a role, so roles chain; a cycle
fails with the profiles it visited. A profile that has `role_arn` and static
keys assumes the role, as the AWS CLI does.

### Assuming roles

Web identity and the role rules call STS. `NSmithy.Aws` calls the two
operations it needs, `AssumeRole` and `AssumeRoleWithWebIdentity`, as form
requests to the regional STS endpoint, `https://sts.{region}.{dnsSuffix}`,
with the DNS suffix from the partition table the endpoint functions use.
`AssumeRole` is signed with SigV4 using the source credentials;
`AssumeRoleWithWebIdentity` is unsigned. The region is the client's resolved
region, or `us-east-1` when there is none.

These calls are written by hand rather than generated: `NSmithy.Aws` is the
runtime that generated AWS clients reference, so generating an STS client into
it would make the package's build depend on code generation. The IAM Identity
Center provider already calls its one endpoint the same way.

An STS error fails with an `AwsCredentialsProviderException` carrying the STS
error code and message, for example `AccessDenied` for a role whose trust
policy does not admit the caller.

### Container credentials

`ContainerAwsCredentialsProvider` serves ECS, Fargate, App Runner, CodeBuild,
and EKS Pod Identity, which all expose credentials over HTTP:

- `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI` is a path on
  `http://169.254.170.2`.
- `AWS_CONTAINER_CREDENTIALS_FULL_URI` is a full URL. Plain HTTP is accepted
  only for loopback addresses and the ECS and EKS link-local hosts
  (`169.254.170.2`, `169.254.170.23`, `fd00:ec2::23`).
- `AWS_CONTAINER_AUTHORIZATION_TOKEN`, or the file
  `AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE` names (read on every request, since
  EKS rotates it), is sent as the `Authorization` header.

### Credential processes

`ProcessAwsCredentialsProvider` runs a `credential_process` command and reads
the JSON it writes to standard output (`Version: 1`, `AccessKeyId`,
`SecretAccessKey`, optional `SessionToken` and `Expiration`). A non-zero exit
fails with the command's standard error. The command line is split the way the
AWS CLI splits it, and is not run through a shell.

## Refresh

Every provider returns `AwsCredentials.Expiration` when the credentials
expire. `AwsSigV4AuthScheme` already wraps its provider in
`SmithyCachingIdentityResolver`, which returns cached credentials until they
are within the refresh window of expiring, then refreshes once for all
concurrent callers. Providers therefore fetch on every call and keep no cache
of their own.

## Region

Generated clients for services whose endpoint rules declare `AWS::Region`
default `Region` in the parameterless `{Service}ClientConfig` constructor. The
AWS integration appends the default through `ClientConfigDefaultsSection`:

```csharp
public LambdaClientConfig()
{
    Region = AwsRegion.Resolve();
}
```

`AwsRegion.Resolve()` returns the first of:

1. `AWS_REGION`
2. `AWS_DEFAULT_REGION`
3. `region` in the selected profile of the shared `config` file

and null when none is set, in which case the endpoint rules report the
missing region as they do today. A `Region` the application sets replaces the
default. Resolution reads only the environment and the config file, so
constructing a configuration does no network I/O.

## Testing

- Each provider takes its environment lookup, file paths, `HttpClient`,
  `TimeProvider`, and, for processes, a process runner, so unit tests cover
  every source with fakes: profile resolution including role chains and
  cycles, STS request and error parsing, container URI rules and tokens,
  process output and failures, and chain order and stickiness.
- STS requests are checked for their endpoint, form parameters, and SigV4
  signature against a fake STS that also exercises role chains and errors.
