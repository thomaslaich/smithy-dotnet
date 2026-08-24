namespace NSmithy.Aws;

public sealed class EnvironmentAwsCredentialsProvider(
    Func<string, string?>? getEnvironmentVariable = null
) : IAwsCredentialsProvider
{
    private readonly Func<string, string?> getEnvironmentVariable =
        getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

    public ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var accessKeyId =
            getEnvironmentVariable("AWS_ACCESS_KEY_ID")
            ?? getEnvironmentVariable("AWS_ACCESS_KEY");
        var secretAccessKey =
            getEnvironmentVariable("AWS_SECRET_ACCESS_KEY")
            ?? getEnvironmentVariable("AWS_SECRET_KEY");
        var sessionToken = getEnvironmentVariable("AWS_SESSION_TOKEN");

        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
        {
            var noneConfigured =
                string.IsNullOrWhiteSpace(accessKeyId) && string.IsNullOrWhiteSpace(secretAccessKey);
            throw new AwsCredentialsProviderException(
                nameof(EnvironmentAwsCredentialsProvider),
                "AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY must both be set.",
                isNotConfigured: noneConfigured
            );
        }

        return ValueTask.FromResult(
            new AwsCredentials(
                accessKeyId,
                secretAccessKey,
                string.IsNullOrWhiteSpace(sessionToken) ? null : sessionToken
            )
        );
    }
}
