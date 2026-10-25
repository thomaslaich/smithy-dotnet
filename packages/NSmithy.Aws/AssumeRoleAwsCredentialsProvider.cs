namespace NSmithy.Aws;

/// <summary>
/// Assumes an IAM role with STS <c>AssumeRole</c>, signing the call with the credentials of
/// another provider.
/// </summary>
public sealed class AssumeRoleAwsCredentialsProvider : IAwsCredentialsProvider
{
    private static readonly HttpClient SharedHttpClient = new();

    private readonly IAwsCredentialsProvider source;
    private readonly string roleArn;
    private readonly string? sessionName;
    private readonly string? externalId;
    private readonly TimeSpan? duration;
    private readonly string? region;
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;
    private readonly Func<string, string?> getEnvironmentVariable;

    /// <param name="source">The credentials that assume the role.</param>
    /// <param name="roleArn">The role to assume.</param>
    /// <param name="sessionName">The role session name. Default: a generated name.</param>
    /// <param name="externalId">The external ID the role's trust policy requires, if any.</param>
    /// <param name="duration">How long the credentials last. Default: the role's setting.</param>
    /// <param name="region">The STS region. Default: <see cref="AwsRegion.Resolve"/>, else us-east-1.</param>
    /// <param name="httpClient">The client STS is called with.</param>
    /// <param name="timeProvider">The clock requests are signed with.</param>
    /// <param name="getEnvironmentVariable">Reads an environment variable, for the region.</param>
    public AssumeRoleAwsCredentialsProvider(
        IAwsCredentialsProvider source,
        string roleArn,
        string? sessionName = null,
        string? externalId = null,
        TimeSpan? duration = null,
        string? region = null,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        Func<string, string?>? getEnvironmentVariable = null
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleArn);
        this.source = source;
        this.roleArn = roleArn;
        this.sessionName = sessionName;
        this.externalId = externalId;
        this.duration = duration;
        this.region = region;
        this.httpClient = httpClient ?? SharedHttpClient;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public async ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var sourceCredentials = await source
            .GetCredentialsAsync(cancellationToken)
            .ConfigureAwait(false);
        return await AwsSts
            .AssumeRoleAsync(
                nameof(AssumeRoleAwsCredentialsProvider),
                httpClient,
                timeProvider,
                region ?? AwsRegion.Resolve(getEnvironmentVariable),
                sourceCredentials,
                roleArn,
                sessionName ?? AwsSts.DefaultSessionName(timeProvider),
                externalId,
                duration,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
