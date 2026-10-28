---
title: Smoke tests
description: Generated smoke tests that call a live service with a real client, for operations with @smokeTests.
---

Operations modeled with [`@smokeTests`](https://smithy.io/2.0/additional-specs/smoke-tests.html)
generate runnable smoke tests when `generateSmokeTests` is set in
`smithy-build.json`. The trait comes from
`software.amazon.smithy:smithy-smoke-test-traits`, which a model using it
lists in `maven.dependencies`. Given:

```smithy
use smithy.test#smokeTests

@readonly
@smokeTests([
    {
        id: "ListTablesSuccess"
        params: { Limit: 1 }
        expect: { success: {} }
    }
    {
        id: "ListTablesRejectsANegativeLimit"
        params: { Limit: -1 }
        expect: { failure: { errorId: ValidationException } }
    }
])
operation ListTables { ... }
```

```json title="smithy-build.json"
{
  "version": "1.0",
  "plugins": {
    "csharp-codegen": { "generateSmokeTests": true }
  }
}
```

the client project gains `TablesSmokeTests.All`, one `SmithySmokeTest` per
case. Each test runs against the live service with the configuration it is
given, which supplies the endpoint, credentials, and anything else the client
needs:

```csharp
var config = new TablesClientConfig { Endpoint = new Uri("https://tables.example.com") };

foreach (var test in TablesSmokeTests.All)
{
    var result = await test.RunAsync(config);
    Console.WriteLine($"{result.Id}: {(result.Passed ? "passed" : result.Failure)}");
}
```

The tests need no test framework. In xUnit, one theory runs them all:

```csharp
public static TheoryData<string> Ids => [.. TablesSmokeTests.All.Select(test => test.Id)];

[Theory]
[MemberData(nameof(Ids))]
public async Task SmokeTest(string id)
{
    var result = await TablesSmokeTests.All.Single(test => test.Id == id).RunAsync(Config());
    Assert.True(result.Passed, result.Failure);
}
```

## Results

| `expect` | Passes when |
| --- | --- |
| `success` | The call returns |
| `failure` with `errorId` | The call throws that error, or a `SmithyClientException` with its name as `ErrorCode` when the operation does not model it |
| `failure` | The call throws any error the service returned |

An exception the service did not return, such as a connection failure, fails
every test. `SmithySmokeTestResult.Exception` holds whatever the call threw.
Cancelling the token passed to `RunAsync` cancels the run instead of failing
the test.

## Configuration

A test applies its settings to a copy of the configuration, which
`Configure(config)` returns; the configuration passed in is not changed. To
create the client yourself, for example over your own `HttpClient`, pass a
factory:

```csharp
var result = await test.RunAsync(config, c => new TablesClient(httpClient, c));
```

For AWS services, `vendorParams` of `aws.test#AwsVendorParams` and
`aws.test#S3VendorParams` set the region (default `us-west-2`), FIPS,
dual-stack, the endpoint, and the S3 addressing settings the service's
endpoint rules read. `sigv4aRegionSet` has no effect. Credentials come from
the configuration's auth schemes:

```csharp
var config = new TablesClientConfig
{
    AuthSchemes = { new AwsSigV4AuthScheme(new DefaultAwsCredentialsProvider()) },
};
```
