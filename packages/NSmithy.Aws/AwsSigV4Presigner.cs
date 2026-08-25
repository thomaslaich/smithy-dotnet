using NSmithy.Http;

namespace NSmithy.Aws;

/// <summary>Creates SigV4 presigned request URIs using credentials resolved on demand.</summary>
public sealed class AwsSigV4Presigner(
    string service,
    string region,
    IAwsCredentialsProvider credentialsProvider,
    Uri? endpoint = null,
    TimeProvider? timeProvider = null
)
{
    private readonly IAwsCredentialsProvider credentialsProvider =
        credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));
    private readonly AwsSigV4Signer signer = new(endpoint, service, region, timeProvider);

    public async ValueTask<Uri> PresignAsync(
        SmithyHttpRequest request,
        TimeSpan expires,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var credentials = await credentialsProvider
            .GetCredentialsAsync(cancellationToken)
            .ConfigureAwait(false);
        return signer.Presign(request, credentials, expires);
    }
}
