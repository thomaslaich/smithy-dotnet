# Smoke tests

How `@smithy.test#smokeTests` becomes generated C# smoke tests.

## Overview

A [smoke test](https://smithy.io/2.0/additional-specs/smoke-tests.html) calls
a deployed service with a real client and checks that the call succeeds or
fails with an expected error. It is not a protocol test: nothing is mocked, and
the point is that the client, its configuration, its credentials, and the live
endpoint work together.

This operation of a `Tables` service has two smoke tests. `ListTablesSuccess`
expects a successful call in `us-west-2`; `ListTablesFailure` expects the
service to reject an invalid `Limit` with any error:

```smithy
$version: "2"
namespace example.tables

use aws.test#AwsVendorParams
use smithy.test#smokeTests

@smokeTests([
    {
        id: "ListTablesSuccess"
        params: { Limit: 1 }
        vendorParamsShape: AwsVendorParams
        vendorParams: { region: "us-west-2" }
        expect: { success: {} }
    }
    {
        id: "ListTablesFailure"
        params: { Limit: -1 }
        vendorParamsShape: AwsVendorParams
        vendorParams: { region: "us-west-2" }
        expect: { failure: {} }
    }
])
@readonly
operation ListTables {
    input := {
        Limit: Integer
    }
    output := {
        TableNames: TableNames
    }
}
```

With `generateSmokeTests`, the generated client project gains a
`TablesSmokeTests` class whose `All` list holds both tests. A test project runs
them with whatever credentials and transport the base configuration carries:

```csharp
var config = new TablesClientConfig
{
    AuthSchemes = { new AwsSigV4AuthScheme(new DefaultAwsCredentialsProvider()) },
};

foreach (var test in TablesSmokeTests.All)
{
    var result = await test.RunAsync(config);
    Console.WriteLine($"{result.Id}: {(result.Passed ? "passed" : result.Failure)}");
}
```

## Goals

- Every `@smokeTests` case of the service's operations becomes one runnable
  test with its id, operation, tags, and expectation.
- Tests need no test framework. They are data plus a run method, so xUnit,
  NUnit, a console runner, or a deployment check can drive them.
- A test never changes the configuration it is given. Vendor params apply to a
  copy.
- Integrations translate vendor params, since their meaning belongs to the
  vendor: the AWS integration maps `aws.test#AwsVendorParams` and
  `aws.test#S3VendorParams` to the generated configuration.

## Non-goals

- Generating test-framework code such as xUnit classes.
- Asserting on output values. The trait expects only success or failure.
- Supplying credentials. The base configuration carries them.

## Model input

Smoke tests come from the `smithy.test#smokeTests` trait on operations bound
to the service. Each case has:

| Field | Use |
| --- | --- |
| `id` | The test's name, unique within the service |
| `params` | The operation input, rendered as a C# constructor call; absent means an empty input |
| `vendorParamsShape`, `vendorParams` | Client configuration, translated by integrations |
| `expect` | `success`, or `failure` with an optional `errorId` |
| `tags` | Labels a runner can filter on |

## Generated API

`generateSmokeTests` in the `csharp-codegen` settings (default `false`) adds
`{Service}.SmokeTests.Client.g.cs` to a client build:

```csharp
public static class TablesSmokeTests
{
    public static IReadOnlyList<SmithySmokeTest<TablesClientConfig, ITablesClient>> All { get; } =
    [
        new(
            "ListTablesSuccess",
            "ListTables",
            SmithySmokeTestExpectation.Success,
            [],
            static config =>
            {
                var configured = new TablesClientConfig(config);
                configured.Region = "us-west-2";
                return new TablesClient(configured);
            },
            static (client, cancellationToken) =>
                client.ListTablesAsync(new ListTablesInput(limit: 1), cancellationToken)
        ),
        new(
            "ListTablesFailure",
            "ListTables",
            SmithySmokeTestExpectation.Failure(null, static exception => exception is SmithyClientException),
            [],
            ...
        ),
    ];
}
```

The class exists only for services with at least one smoke test.

### Expectations

| `expect` | Passes when |
| --- | --- |
| `success` | The call returns |
| `failure` with `errorId` | The call throws that error: its generated exception when the operation or service binds it, otherwise a `SmithyClientException` whose `ErrorCode` is the error's name |
| `failure` without `errorId` | The call throws any error the service returned: a `SmithyClientException` or one of the operation's and service's modeled errors |

A failure the service did not return, such as a DNS or TLS error, fails the
test either way: a smoke test checks the service's answer, not the network.
Cancellation through the caller's token is not a result; it propagates.

## Runtime

`NSmithy.Client` holds the runtime types:

- `SmithySmokeTest<TConfig, TClient>`: `Id`, `Operation`, `Expectation`,
  `Tags`, and `RunAsync(TConfig config, CancellationToken)`, which creates a
  client from a configured copy, invokes the operation, disposes the client, and
  returns a result. An overload takes a `Func<TConfig, TClient>` in place of the
  generated constructor, so a runner can share one `HttpClient` across the tests
  or run them over a fake handler; the test disposes the client, not what the
  factory captured.
- `SmithySmokeTestExpectation`: `Success`, or `Failure(errorId, matches)`.
- `SmithySmokeTestResult`: `Id`, `Passed`, `Failure` (why it did not pass), and
  `Exception` (what the call threw, if anything).

## Vendor params

The generator emits the body of each test's configuration lambda through
`SmokeTestConfigSection(service, operation, testCase)`. It is empty unless an
integration appends statements to it.

The AWS integration reads the case's vendor params, fills in the defaults the
vendor params shape declares, and sets each configuration property the
service's endpoint rules declare as a built-in:

| Vendor param | Configuration |
| --- | --- |
| `region` (default `us-west-2`) | `Region` (`AWS::Region`) |
| `useFips`, `useDualstack` | `UseFips`, `UseDualStack` |
| `useAccountIdRouting: false` | `AccountIdEndpointMode = "disabled"` |
| `uri` | `Endpoint` |
| S3 `useAccelerate`, `useGlobalEndpoint`, `forcePathStyle`, `useArnRegion` | `Accelerate`, `UseGlobalEndpoint`, `ForcePathStyle`, `UseArnRegion` |
| S3 `useMultiRegionAccessPoints: false` | `DisableMultiRegionAccessPoints = true` |

`sigv4aRegionSet` has no effect, since NSmithy does not sign SigV4a.

## Testing

- Codegen tests render a model with success, failure, and `errorId` cases and
  check the generated class, including the AWS vendor params mapping.
- Runtime tests drive `SmithySmokeTest` with fake clients through each
  expectation, cancellation, and a non-service exception.
- A generated test model in `NSmithy.Tests` runs its smoke tests against an
  in-memory transport.
