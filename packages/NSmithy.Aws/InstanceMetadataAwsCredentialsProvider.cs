using System.Net;
using System.Text.Json.Serialization;

namespace NSmithy.Aws;

/// <summary>Loads EC2 role credentials through IMDSv2, with the standard optional IMDSv1 fallback.</summary>
public sealed class InstanceMetadataAwsCredentialsProvider : IAwsCredentialsProvider
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly Uri DefaultEndpoint = new("http://169.254.169.254/");

    private readonly HttpClient httpClient;
    private readonly Uri endpoint;
    private readonly bool allowV1Fallback;
    private readonly Func<string, string?> getEnvironmentVariable;

    public InstanceMetadataAwsCredentialsProvider(
        HttpClient? httpClient = null,
        Uri? endpoint = null,
        bool allowV1Fallback = true,
        Func<string, string?>? getEnvironmentVariable = null
    )
    {
        this.httpClient = httpClient ?? SharedHttpClient;
        this.endpoint = endpoint ?? DefaultEndpoint;
        if (!this.endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("IMDS endpoint must be absolute.", nameof(endpoint));
        }
        this.allowV1Fallback = allowV1Fallback;
        this.getEnvironmentVariable =
            getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public async ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (
            string.Equals(
                getEnvironmentVariable("AWS_EC2_METADATA_DISABLED"),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new AwsCredentialsProviderException(
                nameof(InstanceMetadataAwsCredentialsProvider),
                "EC2 instance metadata is disabled by AWS_EC2_METADATA_DISABLED.",
                isNotConfigured: true
            );
        }

        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        var roleName = (
            await GetStringAsync("latest/meta-data/iam/security-credentials/", token, cancellationToken)
                .ConfigureAwait(false)
        )
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(roleName))
        {
            throw new AwsCredentialsProviderException(
                nameof(InstanceMetadataAwsCredentialsProvider),
                "EC2 instance metadata did not return an IAM role name.",
                isNotConfigured: true
            );
        }

        var json = await GetStringAsync(
                "latest/meta-data/iam/security-credentials/" + Uri.EscapeDataString(roleName),
                token,
                cancellationToken
            )
            .ConfigureAwait(false);
        var document = System.Text.Json.JsonSerializer.Deserialize<ImdsCredentials>(json);
        if (
            document is null
            || !string.Equals(document.Code, "Success", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.AccessKeyId)
            || string.IsNullOrWhiteSpace(document.SecretAccessKey)
        )
        {
            throw new AwsCredentialsProviderException(
                nameof(InstanceMetadataAwsCredentialsProvider),
                "EC2 instance metadata returned an invalid credentials document."
            );
        }
        return new AwsCredentials(
            document.AccessKeyId,
            document.SecretAccessKey,
            document.Token,
            document.Expiration
        );
    }

    private async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Resolve("latest/api/token"));
        request.Headers.TryAddWithoutValidation("X-aws-ec2-metadata-token-ttl-seconds", "21600");
        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        if (
            IsV1FallbackEnabled()
            && response.StatusCode
                is HttpStatusCode.Forbidden
                    or HttpStatusCode.NotFound
                    or HttpStatusCode.MethodNotAllowed
        )
        {
            return null;
        }
        throw new AwsCredentialsProviderException(
            nameof(InstanceMetadataAwsCredentialsProvider),
            $"IMDSv2 token request returned HTTP {(int)response.StatusCode}."
        );
    }

    private async Task<string> GetStringAsync(
        string path,
        string? token,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Resolve(path));
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.TryAddWithoutValidation("X-aws-ec2-metadata-token", token);
        }
        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AwsCredentialsProviderException(
                nameof(InstanceMetadataAwsCredentialsProvider),
                $"Instance metadata request '{path}' returned HTTP {(int)response.StatusCode}.",
                isNotConfigured: response.StatusCode == HttpStatusCode.NotFound
            );
        }
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private Uri Resolve(string path) => new(endpoint, path);

    private bool IsV1FallbackEnabled() =>
        allowV1Fallback
        && !string.Equals(
            getEnvironmentVariable("AWS_EC2_METADATA_V1_DISABLED"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    private sealed record ImdsCredentials(
        [property: JsonPropertyName("Code")] string? Code,
        [property: JsonPropertyName("AccessKeyId")] string? AccessKeyId,
        [property: JsonPropertyName("SecretAccessKey")] string? SecretAccessKey,
        [property: JsonPropertyName("Token")] string? Token,
        [property: JsonPropertyName("Expiration")] DateTimeOffset? Expiration
    );
}
