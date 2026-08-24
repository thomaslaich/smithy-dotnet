namespace NSmithy.Aws;

/// <summary>
/// Resolves AWS credentials in SDK-compatible order: environment, shared profile (including cached
/// IAM Identity Center/SSO sessions), then EC2 instance metadata.
/// </summary>
public sealed class DefaultAwsCredentialsProvider : IAwsCredentialsProvider
{
    private readonly IAwsCredentialsProvider[] providers;

    public DefaultAwsCredentialsProvider(IEnumerable<IAwsCredentialsProvider>? providers = null)
    {
        this.providers =
        [
            .. providers
                ??
                [
                    new EnvironmentAwsCredentialsProvider(),
                    new ProfileAwsCredentialsProvider(),
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
        List<Exception>? unavailable = null;
        foreach (var provider in providers)
        {
            try
            {
                return await provider.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (AwsCredentialsProviderException exception) when (exception.IsNotConfigured)
            {
                (unavailable ??= []).Add(exception);
            }
        }

        throw new AwsCredentialsProviderException(
            nameof(DefaultAwsCredentialsProvider),
            "No AWS credentials were found in the environment, shared profile, or instance metadata.",
            innerException: unavailable is null ? null : new AggregateException(unavailable)
        );
    }
}
