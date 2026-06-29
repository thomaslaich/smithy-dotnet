using NSmithy.Client;

namespace NSmithy.Aws;

public sealed class AwsSigV4AuthScheme(
    string service,
    string region,
    IAwsCredentialsProvider credentialsProvider
) : ISmithyAuthScheme
{
    private readonly string service = string.IsNullOrWhiteSpace(service)
        ? throw new ArgumentException("Service must be set.", nameof(service))
        : service;

    private readonly string region = string.IsNullOrWhiteSpace(region)
        ? throw new ArgumentException("Region must be set.", nameof(region))
        : region;

    private readonly IAwsCredentialsProvider credentialsProvider =
        credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));

    public string SchemeId => "aws.auth#sigv4";

    public IClientInterceptor CreateInterceptor(SmithyAuthSchemeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new AwsSigV4Interceptor(context.Endpoint, service, region, credentialsProvider);
    }
}
