using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NSmithy.Aws;

/// <summary>
/// Exchanges an AWS CLI/IAM Identity Center cached access token for role credentials.
/// Interactive SSO login remains the CLI's responsibility.
/// </summary>
public sealed class SsoAwsCredentialsProvider : IAwsCredentialsProvider
{
    private readonly string accountId;
    private readonly string roleName;
    private readonly string region;
    private readonly string startUrl;
    private readonly string cacheDirectory;
    private readonly HttpClient httpClient;
    private readonly TimeProvider timeProvider;

    public SsoAwsCredentialsProvider(
        string accountId,
        string roleName,
        string region,
        string startUrl,
        string cacheDirectory,
        HttpClient httpClient,
        TimeProvider? timeProvider = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(startUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        this.accountId = accountId;
        this.roleName = roleName;
        this.region = region;
        this.startUrl = startUrl;
        this.cacheDirectory = cacheDirectory;
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var token = FindCachedToken();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://portal.sso.{region}.amazonaws.com/federation/credentials?account_id={Uri.EscapeDataString(accountId)}&role_name={Uri.EscapeDataString(roleName)}"
        );
        request.Headers.TryAddWithoutValidation("x-amz-sso_bearer_token", token);
        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AwsCredentialsProviderException(
                nameof(SsoAwsCredentialsProvider),
                $"IAM Identity Center returned HTTP {(int)response.StatusCode} while retrieving role credentials."
            );
        }

        var payload = await response.Content
            .ReadFromJsonAsync<SsoRoleCredentialsResponse>(cancellationToken)
            .ConfigureAwait(false);
        var credentials = payload?.RoleCredentials;
        if (
            credentials is null
            || string.IsNullOrWhiteSpace(credentials.AccessKeyId)
            || string.IsNullOrWhiteSpace(credentials.SecretAccessKey)
        )
        {
            throw new AwsCredentialsProviderException(
                nameof(SsoAwsCredentialsProvider),
                "IAM Identity Center returned an invalid role-credentials response."
            );
        }

        return new AwsCredentials(
            credentials.AccessKeyId,
            credentials.SecretAccessKey,
            credentials.SessionToken,
            DateTimeOffset.FromUnixTimeMilliseconds(credentials.Expiration)
        );
    }

    private string FindCachedToken()
    {
        if (!Directory.Exists(cacheDirectory))
        {
            throw MissingToken();
        }

        SsoTokenCacheEntry? selected = null;
        foreach (var path in Directory.EnumerateFiles(cacheDirectory, "*.json"))
        {
            try
            {
                var entry = System.Text.Json.JsonSerializer.Deserialize<SsoTokenCacheEntry>(
                    File.ReadAllText(path)
                );
                if (
                    entry is null
                    || string.IsNullOrWhiteSpace(entry.AccessToken)
                    || !string.Equals(entry.StartUrl, startUrl, StringComparison.Ordinal)
                    || !TryParseExpiration(entry.ExpiresAt, out var expiration)
                    || expiration <= timeProvider.GetUtcNow()
                )
                {
                    continue;
                }
                if (
                    selected is null
                    || ParseExpiration(entry.ExpiresAt) > ParseExpiration(selected.ExpiresAt)
                )
                {
                    selected = entry;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // The CLI cache directory can contain unrelated or partially replaced JSON files.
            }
            catch (IOException)
            {
                // A concurrent CLI refresh may replace a cache file while it is being scanned.
            }
        }

        return selected?.AccessToken ?? throw MissingToken();
    }

    private static bool TryParseExpiration(string? value, out DateTimeOffset expiration) =>
        DateTimeOffset.TryParse(
            value?.Replace("UTC", "Z", StringComparison.OrdinalIgnoreCase),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out expiration
        );

    private static DateTimeOffset ParseExpiration(string? value) =>
        TryParseExpiration(value, out var expiration) ? expiration : DateTimeOffset.MinValue;

    private AwsCredentialsProviderException MissingToken() =>
        new(
            nameof(SsoAwsCredentialsProvider),
            $"No unexpired IAM Identity Center token for '{startUrl}' was found in '{cacheDirectory}'."
        );

    private sealed record SsoTokenCacheEntry(
        [property: JsonPropertyName("startUrl")] string? StartUrl,
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("expiresAt")] string? ExpiresAt
    );

    private sealed record SsoRoleCredentialsResponse(
        [property: JsonPropertyName("roleCredentials")] SsoRoleCredentials? RoleCredentials
    );

    private sealed record SsoRoleCredentials(
        [property: JsonPropertyName("accessKeyId")] string AccessKeyId,
        [property: JsonPropertyName("secretAccessKey")] string SecretAccessKey,
        [property: JsonPropertyName("sessionToken")] string? SessionToken,
        [property: JsonPropertyName("expiration")] long Expiration
    );
}
