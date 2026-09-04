# AWS LocalStack example

This example exercises one generated AWS client for each AWS HTTP protocol supported by NSmithy:

- AWS JSON 1.0: DynamoDB `ListTables`
- AWS restXml: S3 `ListBuckets`
- AWS restJson1: Lambda `ListFunctions`
- AWS Query: SQS `ListQueues`
- EC2 Query: EC2 `DescribeRegions`

The `client` project contains the generated clients. On startup, the LocalStack
init hook at `init/ready.d/seed.sh` adds sample resources so the `List*` calls
return populated responses.

## Prerequisites

- .NET 10 SDK
- `just`, or the repository toolchain through `devenv shell`
- Docker

The container image is pinned to `localstack/localstack:3.8`, the last community
release that runs DynamoDB, S3, and Lambda without a `LOCALSTACK_AUTH_TOKEN`.

## Build

Run all commands in this README from the repository root. First build the local
packages and examples:

```bash
just build
just pack
just refresh-examples
```

## Run

Start LocalStack:

```bash
docker compose -f examples/aws-localstack/compose.yaml up
```

The example also includes `examples/aws-localstack/devenv.nix` for users working
from that directory.

In another shell, run the client:

```bash
dotnet run --project examples/aws-localstack/client
```

Expected output:

```text
AWS JSON / DynamoDB ListTables: 2 table(s) [Authors, Books]
restXml / S3 ListBuckets: 2 bucket(s) [nsmithy-demo-assets, nsmithy-demo-logs]
restJson1 / Lambda ListFunctions: 1 function(s) [nsmithy-greeter]
AWS Query / SQS ListQueues: 2 queue(s) [...nsmithy-demo-events, ...nsmithy-demo-jobs]
EC2 Query / EC2 DescribeRegions: one or more LocalStack regions
```

## Stop

Stop LocalStack with Ctrl+C, then remove the Compose resources:

```bash
docker compose -f examples/aws-localstack/compose.yaml down
```

## Generated client configuration

The generated clients take the endpoint directly and a configuration object for
authentication schemes:

```csharp
using var client = new DynamoDB20120810Client(
    new Uri("http://localhost:4566"),
    new()
    {
        AuthSchemes = { new AwsSigV4AuthScheme("dynamodb", "us-east-1", credentials) },
    }
);
```
