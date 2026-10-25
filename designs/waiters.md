# Waiters

How `@smithy.waiters#waitable` becomes generated C# waiters.

## Overview

A [waiter](https://smithy.io/2.0/additional-specs/waiters.html) polls an
operation until a resource reaches a state: a table exists, an instance is
running. The model names the waiter and lists its acceptors; each acceptor
matches the operation's output or error and says whether the waiter succeeds,
fails, or keeps polling.

This operation of a `Tables` service has two waiters. `TableExists` succeeds
once the table is active and keeps polling while it does not exist yet;
`TableNotExists` succeeds once describing the table fails with
`ResourceNotFoundException`:

```smithy
$version: "2"
namespace example.tables

use smithy.waiters#waitable

@readonly
@waitable(
    TableExists: {
        documentation: "Waits until the table exists and is active."
        minDelay: 20
        acceptors: [
            {
                state: "success"
                matcher: {
                    output: { path: "Table.TableStatus", expected: "ACTIVE", comparator: "stringEquals" }
                }
            }
            {
                state: "retry"
                matcher: { errorType: "ResourceNotFoundException" }
            }
        ]
    }
    TableNotExists: {
        documentation: "Waits until the table no longer exists."
        minDelay: 20
        acceptors: [
            {
                state: "success"
                matcher: { errorType: "ResourceNotFoundException" }
            }
        ]
    }
)
operation DescribeTable {
    input := {
        @required
        TableName: String
    }
    output := {
        Table: TableDescription
    }
    errors: [ResourceNotFoundException]
}

structure TableDescription {
    TableName: String
    TableStatus: TableStatus
}

enum TableStatus {
    CREATING
    ACTIVE
    DELETING
}

@error("client")
structure ResourceNotFoundException {
    message: String
}
```

An acceptor's matcher reads the output through a JMESPath `path`, matches a
modeled or unmodeled error by name with `errorType`, or matches any success or
error with `success`. Acceptors are tested in order after every attempt.

NSmithy generates one method per waiter on the client. Acceptors compile to C#
at build time, so polling evaluates no JMESPath at runtime:

```csharp
var result = await tables.WaitUntilTableExistsAsync(
    new DescribeTableInput { TableName = "orders" },
    maxWaitTime: TimeSpan.FromMinutes(5));

Console.WriteLine(result.Output?.Table?.TableStatus);
```

Each poll is a normal operation call, so auth, retries, endpoint resolution,
interceptors, and telemetry apply to every attempt.

## Goals

- Every waiter of a supported service generates a method, with no hand-written
  acceptors.
- Acceptors are plain C# over the generated input and output types: no
  reflection, no runtime JMESPath, trimming- and AOT-safe.
- Waiting follows the specification's acceptor order, retry delays, and
  timeout rules.
- Waiters work on any `I{Service}Client`, including fake clients.

## Non-goals

- A general JMESPath engine. The generator compiles the subset waiters use
  (see [Expressions](#expressions)); an expression outside it fails code
  generation and names the waiter.
- Waiters on servers. Waiters are a client feature.

## Model input

The generator reads `WaitableTrait` from `smithy-waiters`, which is part of
the bundled codegen repository and on the codegen classpath next to
`smithy-csharp-codegen`. Smithy's waiter validators run before generation, so
paths are type-checked against the operation's shapes and every waiter has at
least one success acceptor.

A waiter has:

| Field | Use |
| --- | --- |
| Name | The method name, `WaitUntil{Name}Async` |
| `documentation` | The method's XML documentation |
| `acceptors` | Compiled to the waiter's acceptor table, in order |
| `minDelay`, `maxDelay` | Default delays, 2 and 120 seconds unless modeled |
| `deprecated` | `[Obsolete]` on the method |

## Generated API

`I{Service}Client` and `{Service}Client` get, per waiter:

```csharp
/// <summary>Waits until the table exists.</summary>
Task<SmithyWaiterResult<DescribeTableOutput>> WaitUntilTableExistsAsync(
    DescribeTableInput input,
    TimeSpan maxWaitTime,
    CancellationToken cancellationToken = default);

Task<SmithyWaiterResult<DescribeTableOutput>> WaitUntilTableExistsAsync(
    DescribeTableInput input,
    SmithyWaiterOptions options,
    CancellationToken cancellationToken = default);
```

The maximum wait time is required: the specification leaves it to the caller,
and a waiter without one may poll forever. `SmithyWaiterOptions` also
overrides the modeled delays:

```csharp
public sealed record SmithyWaiterOptions(TimeSpan MaxWaitTime)
{
    public TimeSpan? MinDelay { get; init; }
    public TimeSpan? MaxDelay { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
```

The generated body calls the client's own operation method, so a fake client
or any other `I{Service}Client` implementation waits the same way:

```csharp
public Task<SmithyWaiterResult<DescribeTableOutput>> WaitUntilTableExistsAsync(
    DescribeTableInput input,
    SmithyWaiterOptions options,
    CancellationToken cancellationToken = default) =>
    SmithyWaiter.WaitAsync(
        "TableExists",
        DescribeTableAsync,
        input,
        TablesWaiters.TableExists,
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(120),
        options,
        cancellationToken);
```

### Outcomes

| Outcome | Result |
| --- | --- |
| A `success` acceptor matches | `SmithyWaiterResult<TOutput>` with the matched `Output` or `Exception`, and `Attempts` |
| A `failure` acceptor matches | `SmithyWaiterFailureException` |
| The call fails and no acceptor matches | `SmithyWaiterFailureException`, with the call's exception as `InnerException` |
| The maximum wait time runs out | `SmithyWaiterTimeoutException` |
| `cancellationToken` is cancelled | `OperationCanceledException` |

Both waiter exceptions derive from `SmithyWaiterException`, which carries the
waiter name, the number of attempts, and the last output or exception.

## Runtime

`NSmithy.Client` holds the waiter loop and its types:

```csharp
public enum SmithyWaiterState { Success, Failure, Retry }

public sealed record SmithyWaiterAcceptor<TInput, TOutput>(
    SmithyWaiterState State,
    Func<TInput, TOutput?, Exception?, bool> Matches);

public static class SmithyWaiter
{
    public static Task<SmithyWaiterResult<TOutput>> WaitAsync<TInput, TOutput>(
        string name,
        Func<TInput, CancellationToken, Task<TOutput>> call,
        TInput input,
        IReadOnlyList<SmithyWaiterAcceptor<TInput, TOutput>> acceptors,
        TimeSpan minDelay,
        TimeSpan maxDelay,
        SmithyWaiterOptions options,
        CancellationToken cancellationToken);
}
```

The first attempt runs immediately. After each attempt the acceptors are
tested in order and the first match decides. With no match, an attempt that
returned output is retried and one that threw fails the waiter.

Delays follow the specification. For attempt `n` (starting at 1):

```text
ceiling = log2(maxDelay / minDelay) + 1
delay   = n > ceiling ? maxDelay : minDelay * 2^(n - 1)
delay   = random(minDelay, delay)
if remaining - delay <= minDelay: delay = remaining - minDelay
```

The waiter times out when the remaining time is at most `minDelay` before a
retry. Delays and the clock come from `options.TimeProvider`, so tests run
waiters on a fake clock.

### Error codes

`errorType` acceptors match by error name. A modeled error matches by its
shape name or shape id. Waiters also name errors the operation does not model,
such as EC2's `InvalidInstanceID.NotFound`, so an unmodeled error carries the
wire error code its protocol read: `SmithyClientException.ErrorCode`. Each
client protocol already reads that code to select a modeled error (`__type`,
`X-Amzn-Errortype`, a query `Code` element); it passes it on when no modeled
error matches.

## Expressions

The generator parses each `path` with `smithy-jmespath` and renders it as a C#
expression over the typed input and output, reusing the renderer behind
`@operationContextParams`. The supported subset covers every waiter in the
AWS models:

| JMESPath | Generated C# |
| --- | --- |
| Field, sub-expression | Member access with `?.` |
| `[*]` and `[]` projections | `Select` over non-null items, dropping null results |
| `[?predicate]` filter | `Where` |
| `.*` over a map | `Values` projection |
| Multi-select list `[a, b]` | An array of the selected values |
| `==`, `!=` | Value equality; enums compare by wire value |
| `<`, `<=`, `>`, `>=` | Numeric comparison; null when either side is not a number |
| `&&`, `\|\|`, `!` | JMESPath truthiness |
| Literals, raw strings | C# constants |
| `length(x)` | String length, list count, or map count |
| `contains(list, x)` | `Contains` |

An expression follows JMESPath's null rules: a missing step yields null, and a
projection skips null elements. `inputOutput` paths start at an object with
`input` and `output` members, rendered over the call's input and output.

Comparators:

| Comparator | Matches when the path's value |
| --- | --- |
| `stringEquals` | is a string (or enum) equal to `expected` |
| `booleanEquals` | is a boolean equal to `expected` |
| `allStringEquals` | is a non-empty list whose items all equal `expected` |
| `anyStringEquals` | is a list with at least one item equal to `expected` |

`success: true` matches any returned output, `success: false` any exception.

## Generated code

Each service with waiters gets `{Service}.Waiters.Client.g.cs`, holding a
static `{Service}Waiters` class with one acceptor table per waiter:

```csharp
internal static class TablesWaiters
{
    // TableExists
    internal static readonly SmithyWaiterAcceptor<DescribeTableInput, DescribeTableOutput>[] TableExists =
    [
        // output: Table.TableStatus stringEquals "ACTIVE"
        new(SmithyWaiterState.Success,
            static (input, output, error) => output?.Table?.TableStatus?.Value == "ACTIVE"),
        // errorType: ResourceNotFoundException
        new(SmithyWaiterState.Retry,
            static (input, output, error) => error is ResourceNotFoundException),
    ];
}
```

Each acceptor carries its matcher as a comment, so a waiter that misbehaves
can be traced to the acceptor that decided.

## Testing

- **Generator tests** check the rendered C# for each expression form,
  comparator, and matcher on small inline models.
- **Runtime tests** cover acceptor order, the delay schedule, timeout, and
  cancellation on a fake `TimeProvider`.
- **Model coverage**: every waiter in the AWS models compiles; the
  evil-aws-net sweep generates them all.
- **Behavior tests** run generated waiters against fake clients that return a
  scripted sequence of outputs and errors.
