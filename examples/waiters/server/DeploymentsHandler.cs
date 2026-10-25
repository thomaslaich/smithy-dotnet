using System.Collections.Concurrent;
using Example.Deployments;

/// <summary>
/// Deployments that take a while: each one is pending for two seconds, then runs its build and
/// rollout steps for two seconds each. A deployment created with <c>fail</c> fails its rollout.
/// </summary>
internal sealed class DeploymentsHandler(TimeProvider time) : IDeploymentsServiceHandler
{
    private static readonly TimeSpan StepDuration = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<string, Deployment> Store = new();

    public Task<CreateDeploymentOutput> CreateDeploymentAsync(
        CreateDeploymentInput input,
        CancellationToken cancellationToken = default
    )
    {
        var id = $"dep-{Guid.NewGuid().ToString("N")[..8]}";
        Store[id] = new Deployment(input.Release, input.Fail ?? false, time.GetUtcNow());
        return Task.FromResult(new CreateDeploymentOutput(id, DeploymentStatus.PENDING));
    }

    public Task<GetDeploymentOutput> GetDeploymentAsync(
        GetDeploymentInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (!Store.TryGetValue(input.DeploymentId, out var deployment))
        {
            throw new DeploymentNotFound($"No deployment {input.DeploymentId}.");
        }

        var elapsed = time.GetUtcNow() - deployment.Created;
        var build = StepStatus(elapsed - StepDuration, failed: false);
        var rollout = StepStatus(elapsed - 2 * StepDuration, deployment.Fail);
        var status =
            elapsed < StepDuration ? DeploymentStatus.PENDING
            : rollout == DeploymentStatus.SUCCEEDED ? DeploymentStatus.SUCCEEDED
            : rollout == DeploymentStatus.FAILED ? DeploymentStatus.FAILED
            : DeploymentStatus.RUNNING;

        return Task.FromResult(
            new GetDeploymentOutput(
                input.DeploymentId,
                status,
                [new Step("build", build), new Step("rollout", rollout)]
            )
        );
    }

    /// <summary>The status of a step that started <paramref name="running"/> ago.</summary>
    private static DeploymentStatus StepStatus(TimeSpan running, bool failed) =>
        running < TimeSpan.Zero ? DeploymentStatus.PENDING
        : running < StepDuration ? DeploymentStatus.RUNNING
        : failed ? DeploymentStatus.FAILED
        : DeploymentStatus.SUCCEEDED;

    private sealed record Deployment(string Release, bool Fail, DateTimeOffset Created);
}
