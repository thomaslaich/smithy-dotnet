using NSmithy.Client;

namespace NSmithy.Aws;

/// <summary>
/// The <c>aws.auth#sigv4</c> auth scheme. The signing name and region come from the resolved
/// endpoint, falling back to the ones passed here.
/// </summary>
public sealed class AwsSigV4AuthScheme : ISmithyAuthScheme
{
    internal const string Id = "aws.auth#sigv4";

    /// <summary>
    /// A scheme for clients with modeled endpoint rules, whose endpoints carry the signing name and
    /// the configured region.
    /// </summary>
    public AwsSigV4AuthScheme(
        IAwsCredentialsProvider credentialsProvider,
        TimeProvider? timeProvider = null
    )
        : this(credentialsProvider, new AwsSigV4Signer(timeProvider), timeProvider) { }

    /// <summary>
    /// A scheme that signs with <paramref name="service"/> and <paramref name="region"/> wherever
    /// the resolved endpoint names no signing scope.
    /// </summary>
    public AwsSigV4AuthScheme(
        string service,
        string region,
        IAwsCredentialsProvider credentialsProvider,
        TimeProvider? timeProvider = null
    )
        : this(credentialsProvider, new AwsSigV4Signer(service, region, timeProvider), timeProvider)
    { }

    private AwsSigV4AuthScheme(
        IAwsCredentialsProvider credentialsProvider,
        AwsSigV4Signer signer,
        TimeProvider? timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(credentialsProvider);
        IdentityResolver = new SmithyCachingIdentityResolver(
            new AwsCredentialsIdentityResolver(credentialsProvider),
            timeProvider: timeProvider
        );
        Signer = signer;
    }

    public string SchemeId => Id;

    public ISmithyIdentityResolver IdentityResolver { get; }

    public ISmithySigner Signer { get; }

    private sealed class AwsCredentialsIdentityResolver(IAwsCredentialsProvider provider)
        : ISmithyIdentityResolver
    {
        public async ValueTask<ISmithyIdentity> ResolveIdentityAsync(
            SmithyIdentityProperties properties,
            CancellationToken cancellationToken = default
        ) => await provider.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
    }
}
