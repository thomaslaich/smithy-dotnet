using NSmithy.Client;

namespace NSmithy.Aws;

public sealed class AwsSigV4AuthScheme(
    string service,
    string region,
    IAwsCredentialsProvider credentialsProvider,
    TimeProvider? timeProvider = null
) : ISmithyAuthScheme
{
    public string SchemeId => "aws.auth#sigv4";

    public ISmithyIdentityResolver IdentityResolver { get; } =
        new SmithyCachingIdentityResolver(
            new AwsCredentialsIdentityResolver(
                credentialsProvider
                    ?? throw new ArgumentNullException(nameof(credentialsProvider))
            ),
            timeProvider: timeProvider
        );

    public ISmithySigner Signer { get; } = new AwsSigV4Signer(service, region, timeProvider);

    private sealed class AwsCredentialsIdentityResolver(IAwsCredentialsProvider provider)
        : ISmithyIdentityResolver
    {
        private readonly IAwsCredentialsProvider provider =
            provider ?? throw new ArgumentNullException(nameof(provider));

        public async ValueTask<ISmithyIdentity> ResolveIdentityAsync(
            SmithyIdentityProperties properties,
            CancellationToken cancellationToken = default
        ) =>
            await provider
                .GetCredentialsAsync(cancellationToken)
                .ConfigureAwait(false);
    }
}
