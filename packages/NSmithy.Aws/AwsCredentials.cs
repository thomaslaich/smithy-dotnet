namespace NSmithy.Aws;

public sealed class AwsCredentials
{
    public AwsCredentials(string accessKeyId, string secretAccessKey, string? sessionToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretAccessKey);

        AccessKeyId = accessKeyId;
        SecretAccessKey = secretAccessKey;
        SessionToken = sessionToken;
    }

    public string AccessKeyId { get; }

    public string SecretAccessKey { get; }

    public string? SessionToken { get; }
}
