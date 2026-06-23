# AWS LocalStack example

This example exercises one generated AWS client for each AWS HTTP protocol supported by NSmithy:

- AWS JSON 1.0: DynamoDB `ListTables`
- AWS restXml: S3 `ListBuckets`
- AWS restJson1: Lambda `ListFunctions`

On startup, an init hook (`init/ready.d/seed.sh`) seeds a little data into each
service so the `List*` calls return populated responses, so you should see:

```
AWS JSON / DynamoDB ListTables: 2 table(s) [Authors, Books]
restXml / S3 ListBuckets: 2 bucket(s) [nsmithy-demo-assets, nsmithy-demo-logs]
restJson1 / Lambda ListFunctions: 1 function(s) [nsmithy-greeter]
```

Run it against LocalStack (runs in Docker, so a running Docker daemon is
required — `devenv up` brings the container up via `compose.yaml`). The
container image is pinned to `localstack/localstack:3.8`, the last community
release that runs DynamoDB/S3/Lambda without a `LOCALSTACK_AUTH_TOKEN` license:

```bash
just pack
cd examples/aws-localstack
devenv up   # or: docker compose up
```

In another shell:

```bash
cd examples/aws-localstack
devenv shell
dotnet run --project client
```

The generated clients take the endpoint directly and a config object for auth schemes:

```csharp
using var client = new DynamoDB20120810Client(
    new Uri("http://localhost:4566"),
    new()
    {
        AuthSchemes = { new AwsSigV4AuthScheme("dynamodb", "us-east-1", credentials) },
    }
);
```
