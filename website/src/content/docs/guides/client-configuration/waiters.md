---
title: Waiters
description: Generated waiters that poll an operation until a resource reaches a state, for @waitable operations.
---

Operations modeled with [`@waitable`](https://smithy.io/2.0/additional-specs/waiters.html)
generate one waiter method per waiter. The trait comes from
`software.amazon.smithy:smithy-waiters`, which a model using it lists in
`maven.dependencies`. Given:

```smithy
use smithy.waiters#waitable

@readonly
@waitable(
    TableExists: {
        minDelay: 20
        acceptors: [
            {
                state: "success"
                matcher: { output: { path: "Table.TableStatus", expected: "ACTIVE", comparator: "stringEquals" } }
            }
            {
                state: "retry"
                matcher: { errorType: "ResourceNotFoundException" }
            }
        ]
    }
)
operation DescribeTable { ... }
```

the client gains `WaitUntilTableExistsAsync`, which calls `DescribeTable` until
an acceptor reaches a terminal state:

```csharp
var result = await client.WaitUntilTableExistsAsync(
    new DescribeTableInput { TableName = "orders" },
    maxWaitTime: TimeSpan.FromMinutes(5));

Console.WriteLine(result.Output?.Table?.TableStatus);
```

Every attempt is a normal operation call, so auth, retries, endpoint
resolution, interceptors, and telemetry apply to each one.

## Outcomes

| Outcome | Result |
| --- | --- |
| A `success` acceptor matches | `SmithyWaiterResult<TOutput>` with the matched `Output` or `Exception`, and `Attempts` |
| A `failure` acceptor matches | `SmithyWaiterFailureException` |
| The call fails and no acceptor matches | `SmithyWaiterFailureException`, with the call's exception as `InnerException` |
| The maximum wait time runs out | `SmithyWaiterTimeoutException` |
| The cancellation token is cancelled | `OperationCanceledException` |

A waiter can succeed on an error: a `TableNotExists` waiter succeeds once
`DescribeTable` throws `ResourceNotFoundException`, and its result carries that
exception.

## Delays

Attempts back off exponentially from the waiter's `minDelay` (default 2
seconds) to its `maxDelay` (default 120 seconds), with jitter, as the waiters
specification defines. The waiter times out rather than start a delay that
would leave less than `minDelay` of the maximum wait time.

`SmithyWaiterOptions` overrides the modeled delays and supplies the clock:

```csharp
await client.WaitUntilTableExistsAsync(
    input,
    new SmithyWaiterOptions(TimeSpan.FromMinutes(5))
    {
        MinDelay = TimeSpan.FromSeconds(5),
        MaxDelay = TimeSpan.FromSeconds(30),
    });
```

Set `TimeProvider` to a fake clock to run waiters in tests without waiting.

## Notes

- Acceptor paths are compiled to C# at build time. A path the generator does
  not support fails code generation with a message naming the waiter.
- `errorType` matches a modeled error by its type and any other error by
  `SmithyClientException.ErrorCode`, the error code the protocol read from
  the response.
- Waiters are members of `I{Service}Client` and call the client's own
  operation method, so a [fake client](/smithy-dotnet/guides/client-configuration/fake-clients/)
  that overrides the operation waits over its responses.
