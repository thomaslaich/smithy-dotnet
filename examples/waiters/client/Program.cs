using Example.Deployments;
using NSmithy.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";
using var client = new DeploymentsClient(new Uri(endpoint));

// CreateDeployment returns while the deployment is still pending. The generated waiter polls
// GetDeployment, backing off between 1 and 4 seconds as the model's waiter declares, until the
// deployment succeeds.
var created = await client.CreateDeploymentAsync(new CreateDeploymentInput("v1.2.0"));
Console.WriteLine($"Created {created.DeploymentId}: {created.Status}");

var done = await client.WaitUntilDeploymentCompleteAsync(
    new GetDeploymentInput(created.DeploymentId),
    maxWaitTime: TimeSpan.FromMinutes(1)
);
Console.WriteLine(
    $"{done.Output!.DeploymentId}: {done.Output.Status} after {done.Attempts} attempts"
);

// A deployment whose rollout fails reaches the waiter's failure state, which throws.
var failing = await client.CreateDeploymentAsync(new CreateDeploymentInput("v1.3.0", Fail: true));
Console.WriteLine($"Created {failing.DeploymentId}: {failing.Status}");
try
{
    await client.WaitUntilDeploymentCompleteAsync(
        new GetDeploymentInput(failing.DeploymentId),
        maxWaitTime: TimeSpan.FromMinutes(1)
    );
}
catch (SmithyWaiterFailureException error)
{
    var last = (GetDeploymentOutput)error.LastOutput!;
    var steps = string.Join(", ", last.Steps!.Select(step => $"{step.Name} {step.Status}"));
    Console.WriteLine(
        $"{failing.DeploymentId}: {last.Status} after {error.Attempts} attempts ({steps})"
    );
}

// A waiter that cannot finish in time times out.
var slow = await client.CreateDeploymentAsync(new CreateDeploymentInput("v1.4.0"));
try
{
    await client.WaitUntilDeploymentCompleteAsync(
        new GetDeploymentInput(slow.DeploymentId),
        maxWaitTime: TimeSpan.FromSeconds(3)
    );
}
catch (SmithyWaiterTimeoutException error)
{
    Console.WriteLine(
        $"{slow.DeploymentId}: still {((GetDeploymentOutput)error.LastOutput!).Status} when the 3-second wait ran out"
    );
}
