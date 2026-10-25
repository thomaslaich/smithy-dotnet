namespace NSmithy.Aws;

/// <summary>
/// Exchanges a web identity token, such as an EKS service account or GitHub Actions OIDC token,
/// for role credentials with STS <c>AssumeRoleWithWebIdentity</c>. Without arguments it reads
/// <c>AWS_WEB_IDENTITY_TOKEN_FILE</c>, <c>AWS_ROLE_ARN</c>, and <c>AWS_ROLE_SESSION_NAME</c>.
/// The token file is read on every call, since its issuer rotates it.
/// </summary>
public sealed class WebIdentityAwsCredentialsProvider : IAwsCredentialsProvider
{
    private static readonly HttpClient SharedHttpClient = new();

    private readonly string? tokenFile;
    private readonly string? roleArn;
    private readonly string? sessionName;
    private readonly string? region;
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;
    private readonly Func<string, string?> getEnvironmentVariable;

    public WebIdentityAwsCredentialsProvider(
        string? tokenFile = null,
        string? roleArn = null,
        string? sessionName = null,
        string? region = null,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        Func<string, string?>? getEnvironmentVariable = null
    )
    {
        this.tokenFile = tokenFile;
        this.roleArn = roleArn;
        this.sessionName = sessionName;
        this.region = region;
        this.httpClient = httpClient ?? SharedHttpClient;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public async ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var file = tokenFile ?? NonEmpty(getEnvironmentVariable("AWS_WEB_IDENTITY_TOKEN_FILE"));
        var role = roleArn ?? NonEmpty(getEnvironmentVariable("AWS_ROLE_ARN"));
        if (file is null || role is null)
        {
            throw new AwsCredentialsProviderException(
                nameof(WebIdentityAwsCredentialsProvider),
                "AWS_WEB_IDENTITY_TOKEN_FILE and AWS_ROLE_ARN must both be set.",
                isNotConfigured: file is null && role is null
            );
        }

        string token;
        try
        {
            token = (
                await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)
            ).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AwsCredentialsProviderException(
                nameof(WebIdentityAwsCredentialsProvider),
                $"The web identity token file {file} could not be read.",
                innerException: exception
            );
        }

        return await AwsSts
            .AssumeRoleWithWebIdentityAsync(
                nameof(WebIdentityAwsCredentialsProvider),
                httpClient,
                region ?? AwsRegion.Resolve(getEnvironmentVariable),
                token,
                role,
                sessionName
                    ?? NonEmpty(getEnvironmentVariable("AWS_ROLE_SESSION_NAME"))
                    ?? AwsSts.DefaultSessionName(timeProvider),
                duration: null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
