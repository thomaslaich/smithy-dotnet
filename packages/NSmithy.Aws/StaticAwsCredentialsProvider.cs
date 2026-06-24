namespace NSmithy.Aws;

public sealed class StaticAwsCredentialsProvider(AwsCredentials credentials)
    : IAwsCredentialsProvider
{
    private readonly AwsCredentials credentials =
        credentials ?? throw new ArgumentNullException(nameof(credentials));

    public ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(credentials);
    }
}
