namespace NSmithy.Aws;

/// <summary>
/// Resolves AWS credentials in the AWS SDKs' order: environment, web identity, shared profile,
/// container credentials, then EC2 instance metadata. A source that is not configured is skipped;
/// one that is configured but fails stops the chain with its error. The source that succeeds
/// first answers every later call.
/// </summary>
public sealed class DefaultAwsCredentialsProvider : IAwsCredentialsProvider
{
    private readonly IAwsCredentialsProvider[] providers;
    private IAwsCredentialsProvider? resolved;

    public DefaultAwsCredentialsProvider(IEnumerable<IAwsCredentialsProvider>? providers = null)
    {
        this.providers =
        [
            .. providers
                ??
                [
                    new EnvironmentAwsCredentialsProvider(),
                    new WebIdentityAwsCredentialsProvider(),
                    new ProfileAwsCredentialsProvider(),
                    new ContainerAwsCredentialsProvider(),
                    new InstanceMetadataAwsCredentialsProvider(),
                ],
        ];
        if (this.providers.Length == 0)
        {
            throw new ArgumentException(
                "At least one credentials provider is required.",
                nameof(providers)
            );
        }
    }

    public async ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (Volatile.Read(ref resolved) is { } provider)
        {
            return await provider.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }

        List<Exception>? unavailable = null;
        foreach (var candidate in providers)
        {
            AwsCredentials credentials;
            try
            {
                credentials = await candidate
                    .GetCredentialsAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AwsCredentialsProviderException exception) when (exception.IsNotConfigured)
            {
                (unavailable ??= []).Add(exception);
                continue;
            }
            Volatile.Write(ref resolved, candidate);
            return credentials;
        }

        throw new AwsCredentialsProviderException(
            nameof(DefaultAwsCredentialsProvider),
            "No AWS credentials were found in the environment, a web identity token, the shared profile, the container credentials endpoint, or instance metadata.",
            innerException: unavailable is null ? null : new AggregateException(unavailable)
        );
    }
}
