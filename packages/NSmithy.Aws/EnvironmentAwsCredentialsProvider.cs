namespace NSmithy.Aws;

public sealed class EnvironmentAwsCredentialsProvider : IAwsCredentialsProvider
{
    public ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var accessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var secretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        var sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");

        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
        {
            throw new InvalidOperationException(
                "AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY must be set."
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
