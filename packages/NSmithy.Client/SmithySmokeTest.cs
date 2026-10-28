namespace NSmithy.Client;

/// <summary>
/// A smoke test from a model's <c>@smokeTests</c> trait: one call of an operation against the live
/// service, expected to succeed or to fail with an error the service returns.
/// </summary>
/// <typeparam name="TConfig">The generated client configuration.</typeparam>
/// <typeparam name="TClient">The generated client interface.</typeparam>
public sealed class SmithySmokeTest<TConfig, TClient>
    where TClient : IDisposable
{
    private readonly Func<TConfig, TConfig> configure;
    private readonly Func<TConfig, TClient> createClient;
    private readonly Func<TClient, CancellationToken, Task> invoke;

    /// <param name="id">The test's id, unique within the service.</param>
    /// <param name="operation">The name of the operation the test calls.</param>
    /// <param name="expectation">Whether the call is expected to succeed or fail.</param>
    /// <param name="tags">The test's tags.</param>
    /// <param name="configure">Returns a copy of a configuration with the test's settings applied.</param>
    /// <param name="createClient">Creates the generated client from a configuration.</param>
    /// <param name="invoke">Calls the operation with the test's input.</param>
    public SmithySmokeTest(
        string id,
        string operation,
        SmithySmokeTestExpectation expectation,
        IReadOnlyList<string> tags,
        Func<TConfig, TConfig> configure,
        Func<TConfig, TClient> createClient,
        Func<TClient, CancellationToken, Task> invoke
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(expectation);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(createClient);
        ArgumentNullException.ThrowIfNull(invoke);
        Id = id;
        Operation = operation;
        Expectation = expectation;
        Tags = tags;
        this.configure = configure;
        this.createClient = createClient;
        this.invoke = invoke;
    }

    public string Id { get; }

    public string Operation { get; }

    public SmithySmokeTestExpectation Expectation { get; }

    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// A copy of <paramref name="config"/> with the test's settings, such as its region, applied.
    /// </summary>
    public TConfig Configure(TConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return configure(config);
    }

    /// <summary>
    /// Runs the test with the generated client, created from <see cref="Configure"/>'s copy of
    /// <paramref name="config"/>, which keeps its credentials and is not changed. Cancellation
    /// through <paramref name="cancellationToken"/> propagates instead of failing the test.
    /// </summary>
    public Task<SmithySmokeTestResult> RunAsync(
        TConfig config,
        CancellationToken cancellationToken = default
    ) => RunAsync(config, createClient, cancellationToken);

    /// <summary>
    /// Runs the test with a client <paramref name="createClient"/> creates from
    /// <see cref="Configure"/>'s copy of <paramref name="config"/>, for example over a given
    /// <see cref="HttpClient"/>. The test disposes the client.
    /// </summary>
    public async Task<SmithySmokeTestResult> RunAsync(
        TConfig config,
        Func<TConfig, TClient> createClient,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(createClient);
        ArgumentNullException.ThrowIfNull(config);
        Exception? exception = null;
        using (var client = createClient(Configure(config)))
        {
            try
            {
                await invoke(client, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // A smoke test reports whatever the call threw as its result.
            catch (Exception caught)
#pragma warning restore CA1031
            {
                exception = caught;
            }
        }

        if (Expectation.IsSuccess)
        {
            return exception is null
                ? SmithySmokeTestResult.Pass(Id, null)
                : SmithySmokeTestResult.Fail(
                    Id,
                    $"Expected success, but the call threw {exception.GetType().Name}: {exception.Message}",
                    exception
                );
        }

        if (exception is null)
        {
            return SmithySmokeTestResult.Fail(
                Id,
                $"Expected {Expectation.ErrorId ?? "an error"}, but the call succeeded.",
                null
            );
        }
        return Expectation.Matches(exception)
            ? SmithySmokeTestResult.Pass(Id, exception)
            : SmithySmokeTestResult.Fail(
                Id,
                $"Expected {Expectation.ErrorId ?? "an error from the service"}, but the call threw {exception.GetType().Name}: {exception.Message}",
                exception
            );
    }
}
