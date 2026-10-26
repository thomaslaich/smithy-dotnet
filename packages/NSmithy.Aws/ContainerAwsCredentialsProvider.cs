using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NSmithy.Aws;

/// <summary>
/// Loads credentials from the container credentials endpoint of ECS, Fargate, App Runner,
/// CodeBuild, and EKS Pod Identity, configured by <c>AWS_CONTAINER_CREDENTIALS_RELATIVE_URI</c> or
/// <c>AWS_CONTAINER_CREDENTIALS_FULL_URI</c>, with <c>AWS_CONTAINER_AUTHORIZATION_TOKEN</c> or
/// <c>AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE</c>.
/// </summary>
public sealed class ContainerAwsCredentialsProvider : IAwsCredentialsProvider
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    // ECS serves relative URIs on this link-local host.
    private static readonly Uri EcsEndpoint = new("http://169.254.170.2");

    // Full URIs may use plain HTTP only to reach these hosts, besides loopback addresses: the ECS
    // and EKS Pod Identity agents.
    private static readonly IPAddress[] AllowedHttpHosts =
    [
        IPAddress.Parse("169.254.170.2"),
        IPAddress.Parse("169.254.170.23"),
        IPAddress.Parse("fd00:ec2::23"),
    ];

    private readonly HttpClient httpClient;
    private readonly Func<string, string?> getEnvironmentVariable;

    public ContainerAwsCredentialsProvider(
        HttpClient? httpClient = null,
        Func<string, string?>? getEnvironmentVariable = null
    )
    {
        this.httpClient = httpClient ?? SharedHttpClient;
        this.getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public async ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var uri = Endpoint();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var token = await AuthorizationTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", token);
        }

        ContainerCredentials? credentials;
        try
        {
            using var response = await httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AwsCredentialsProviderException(
                    nameof(ContainerAwsCredentialsProvider),
                    $"The container credentials endpoint {uri} returned HTTP {(int)response.StatusCode}."
                );
            }
            credentials = await response
                .Content.ReadFromJsonAsync(
                    ContainerCredentialsContext.Default.ContainerCredentials,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new AwsCredentialsProviderException(
                nameof(ContainerAwsCredentialsProvider),
                $"The container credentials endpoint {uri} could not be reached.",
                innerException: exception
            );
        }

        if (
            string.IsNullOrWhiteSpace(credentials?.AccessKeyId)
            || string.IsNullOrWhiteSpace(credentials.SecretAccessKey)
        )
        {
            throw new AwsCredentialsProviderException(
                nameof(ContainerAwsCredentialsProvider),
                $"The container credentials endpoint {uri} returned no credentials."
            );
        }
        return new AwsCredentials(
            credentials.AccessKeyId,
            credentials.SecretAccessKey,
            credentials.Token,
            credentials.Expiration
        );
    }

    private Uri Endpoint()
    {
        var relative = getEnvironmentVariable("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI");
        if (!string.IsNullOrWhiteSpace(relative))
        {
            return new Uri(EcsEndpoint, relative);
        }

        var full = getEnvironmentVariable("AWS_CONTAINER_CREDENTIALS_FULL_URI");
        if (string.IsNullOrWhiteSpace(full))
        {
            throw new AwsCredentialsProviderException(
                nameof(ContainerAwsCredentialsProvider),
                "Neither AWS_CONTAINER_CREDENTIALS_RELATIVE_URI nor AWS_CONTAINER_CREDENTIALS_FULL_URI is set.",
                isNotConfigured: true
            );
        }
        if (!Uri.TryCreate(full, UriKind.Absolute, out var uri))
        {
            throw new AwsCredentialsProviderException(
                nameof(ContainerAwsCredentialsProvider),
                $"AWS_CONTAINER_CREDENTIALS_FULL_URI '{full}' is not an absolute URI."
            );
        }
        if (uri.Scheme == Uri.UriSchemeHttp && !IsAllowedHttpHost(uri))
        {
            throw new AwsCredentialsProviderException(
                nameof(ContainerAwsCredentialsProvider),
                $"AWS_CONTAINER_CREDENTIALS_FULL_URI '{full}' uses HTTP to a host that is not a loopback or container credentials address."
            );
        }
        return uri;
    }

    private static bool IsAllowedHttpHost(Uri uri) =>
        uri.IsLoopback
        || (
            IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address)
            && (IPAddress.IsLoopback(address) || AllowedHttpHosts.Contains(address))
        );

    private async Task<string?> AuthorizationTokenAsync(CancellationToken cancellationToken)
    {
        var tokenFile = getEnvironmentVariable("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE");
        if (!string.IsNullOrWhiteSpace(tokenFile))
        {
            try
            {
                return (
                    await File.ReadAllTextAsync(tokenFile, cancellationToken).ConfigureAwait(false)
                ).Trim();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                throw new AwsCredentialsProviderException(
                    nameof(ContainerAwsCredentialsProvider),
                    $"The container authorization token file {tokenFile} could not be read.",
                    innerException: exception
                );
            }
        }
        var token = getEnvironmentVariable("AWS_CONTAINER_AUTHORIZATION_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}

internal sealed record ContainerCredentials(
    string? AccessKeyId,
    string? SecretAccessKey,
    string? Token,
    DateTimeOffset? Expiration
);

[JsonSerializable(typeof(ContainerCredentials))]
internal sealed partial class ContainerCredentialsContext : JsonSerializerContext;
