using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Aws;

/// <summary>
/// Compatibility interceptor for applying SigV4 directly. Generated clients use
/// <see cref="AwsSigV4AuthScheme"/> so identity resolution and signing remain separate.
/// </summary>
public sealed class AwsSigV4Interceptor : IClientInterceptor
{
    private readonly IAwsCredentialsProvider credentialsProvider;
    private readonly AwsSigV4Signer signer;

    public AwsSigV4Interceptor(
        Uri endpoint,
        string service,
        string region,
        IAwsCredentialsProvider credentialsProvider,
        TimeProvider? timeProvider = null
    )
    {
        this.credentialsProvider =
            credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));
        signer = new AwsSigV4Signer(endpoint, service, region, timeProvider);
    }

    public async ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var credentials = await credentialsProvider
            .GetCredentialsAsync(cancellationToken)
            .ConfigureAwait(false);
        return await signer
            .SignAsync(context, request, credentials, cancellationToken)
            .ConfigureAwait(false);
    }
}
