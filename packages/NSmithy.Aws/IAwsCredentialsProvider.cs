namespace NSmithy.Aws;

public interface IAwsCredentialsProvider
{
    ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken = default);
}
