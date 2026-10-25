# Waiters example

A Deployments service built with `aws.protocols#restJson1` whose deployments
take a while. `CreateDeployment` returns at once with a `PENDING` deployment;
the server then runs its build and rollout steps for a few seconds. The model
declares a `DeploymentComplete` waiter on `GetDeployment`:

```smithy
@waitable(
    DeploymentComplete: {
        minDelay: 1
        maxDelay: 4
        acceptors: [
            { state: "success", matcher: { output: { path: "status", expected: "SUCCEEDED", comparator: "stringEquals" } } }
            { state: "failure", matcher: { output: { path: "status", expected: "FAILED", comparator: "stringEquals" } } }
            { state: "failure", matcher: { output: { path: "steps[].status", expected: "FAILED", comparator: "anyStringEquals" } } }
        ]
    }
)
operation GetDeployment { ... }
```

The client calls the generated `WaitUntilDeploymentCompleteAsync`, which polls
`GetDeployment` with backoff until a deployment succeeds, fails, or the wait
runs out.

## Projects

- `contracts`: the Smithy model.
- `server`: generated ASP.NET Core endpoints backed by an in-memory
  `DeploymentsHandler` that advances each deployment with time.
- `client`: starts three deployments and waits for each.

## Build

Run all commands in this README from `examples/waiters`. First build the local
packages and examples:

```bash
just build
just pack
just refresh-examples
```

## Run

Start the server:

```bash
dotnet run --project server --urls http://localhost:5000
```

In another shell, run the client:

```bash
dotnet run --project client -- http://localhost:5000
```

```text
Created dep-849c59a6: PENDING
dep-849c59a6: SUCCEEDED after 5 attempts
Created dep-aac88737: PENDING
dep-aac88737: FAILED after 6 attempts (build SUCCEEDED, rollout FAILED)
dep-e6d6ada6: still RUNNING when the 3-second wait ran out
```

The first deployment reaches the success state. The second fails its rollout,
so a failure acceptor matches and the waiter throws
`SmithyWaiterFailureException`. The third is given only three seconds and
throws `SmithyWaiterTimeoutException`. Attempt counts vary with the jitter
between attempts.
