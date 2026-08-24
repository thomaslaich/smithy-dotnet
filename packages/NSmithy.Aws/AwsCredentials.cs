using NSmithy.Client;

namespace NSmithy.Aws;

public sealed class AwsCredentials : ISmithyIdentity
{
    public AwsCredentials(
        string accessKeyId,
        string secretAccessKey,
        string? sessionToken = null,
        DateTimeOffset? expiration = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretAccessKey);

        AccessKeyId = accessKeyId;
        SecretAccessKey = secretAccessKey;
        SessionToken = sessionToken;
        Expiration = expiration;
    }

    public string AccessKeyId { get; }

    public string SecretAccessKey { get; }

    public string? SessionToken { get; }

    public DateTimeOffset? Expiration { get; }
}
