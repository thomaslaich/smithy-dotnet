namespace NSmithy.Aws;

/// <summary>Loads static or IAM Identity Center credentials from AWS shared profile files.</summary>
public sealed class ProfileAwsCredentialsProvider(
    string? profileName = null,
    string? credentialsPath = null,
    string? configPath = null,
    string? ssoCacheDirectory = null,
    HttpClient? httpClient = null,
    TimeProvider? timeProvider = null,
    Func<string, string?>? getEnvironmentVariable = null
) : IAwsCredentialsProvider
{
    private static readonly HttpClient SharedHttpClient = new();

    private readonly string? profileName = profileName;
    private readonly string? credentialsPath = credentialsPath;
    private readonly string? configPath = configPath;
    private readonly string? ssoCacheDirectory = ssoCacheDirectory;
    private readonly HttpClient httpClient = httpClient ?? SharedHttpClient;
    private readonly Func<string, string?> getEnvironmentVariable =
        getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    ) => GetCredentialsCoreAsync(cancellationToken);

    private async ValueTask<AwsCredentials> GetCredentialsCoreAsync(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selectedProfile =
            profileName
            ?? getEnvironmentVariable("AWS_PROFILE")
            ?? getEnvironmentVariable("AWS_DEFAULT_PROFILE")
            ?? "default";
        var awsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aws"
        );
        var resolvedCredentialsPath =
            credentialsPath
            ?? getEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE")
            ?? Path.Combine(awsDirectory, "credentials");
        var resolvedConfigPath =
            configPath
            ?? getEnvironmentVariable("AWS_CONFIG_FILE")
            ?? Path.Combine(awsDirectory, "config");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        MergeSection(values, ReadIniFile(resolvedConfigPath), ConfigSection(selectedProfile));
        MergeSection(values, ReadIniFile(resolvedCredentialsPath), selectedProfile);

        if (
            values.TryGetValue("aws_access_key_id", out var accessKeyId)
            && values.TryGetValue("aws_secret_access_key", out var secretAccessKey)
        )
        {
            values.TryGetValue("aws_session_token", out var sessionToken);
            return new AwsCredentials(accessKeyId, secretAccessKey, sessionToken);
        }

        if (TryGetSsoSettings(values, resolvedConfigPath, out var sso))
        {
            var provider = new SsoAwsCredentialsProvider(
                sso.AccountId,
                sso.RoleName,
                sso.Region,
                sso.StartUrl,
                ssoCacheDirectory ?? Path.Combine(awsDirectory, "sso", "cache"),
                httpClient,
                timeProvider
            );
            return await provider.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }

        if (values.Count == 0)
        {
            throw new AwsCredentialsProviderException(
                nameof(ProfileAwsCredentialsProvider),
                $"AWS profile '{selectedProfile}' was not found.",
                isNotConfigured: true
            );
        }

        throw new AwsCredentialsProviderException(
            nameof(ProfileAwsCredentialsProvider),
            $"AWS profile '{selectedProfile}' does not contain static credentials or a complete SSO configuration."
        );
    }

    private static Dictionary<string, Dictionary<string, string>> ReadIniFile(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase
        );
        if (!File.Exists(path))
        {
            return sections;
        }

        Dictionary<string, string>? current = null;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }
            if (line[0] == '[' && line[^1] == ']')
            {
                var name = line[1..^1].Trim();
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[name] = current;
                continue;
            }
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (current is not null && equals > 0)
            {
                current[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        }
        return sections;
    }

    private static void MergeSection(
        Dictionary<string, string> destination,
        Dictionary<string, Dictionary<string, string>> sections,
        string section
    )
    {
        if (!sections.TryGetValue(section, out var values))
        {
            return;
        }
        foreach (var value in values)
        {
            destination[value.Key] = value.Value;
        }
    }

    private static string ConfigSection(string profile) =>
        string.Equals(profile, "default", StringComparison.Ordinal)
            ? profile
            : "profile " + profile;

    private static bool TryGetSsoSettings(
        Dictionary<string, string> values,
        string configPath,
        out SsoSettings settings
    )
    {
        if (values.TryGetValue("sso_session", out var session))
        {
            MergeSection(values, ReadIniFile(configPath), "sso-session " + session);
        }
        if (
            values.TryGetValue("sso_start_url", out var startUrl)
            && values.TryGetValue("sso_region", out var region)
            && values.TryGetValue("sso_account_id", out var accountId)
            && values.TryGetValue("sso_role_name", out var roleName)
        )
        {
            settings = new SsoSettings(startUrl, region, accountId, roleName);
            return true;
        }
        settings = default;
        return false;
    }

    private readonly record struct SsoSettings(
        string StartUrl,
        string Region,
        string AccountId,
        string RoleName
    );
}
