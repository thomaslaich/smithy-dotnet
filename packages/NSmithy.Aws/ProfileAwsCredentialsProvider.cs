using System.Globalization;

namespace NSmithy.Aws;

/// <summary>
/// Loads credentials for a profile of the AWS shared <c>config</c> and <c>credentials</c> files:
/// an assumed role (through <c>source_profile</c>, <c>credential_source</c>, or
/// <c>web_identity_token_file</c>), IAM Identity Center, <c>credential_process</c>, or static keys,
/// in that order.
/// </summary>
public sealed class ProfileAwsCredentialsProvider(
    string? profileName = null,
    string? credentialsPath = null,
    string? configPath = null,
    string? ssoCacheDirectory = null,
    HttpClient? httpClient = null,
    TimeProvider? timeProvider = null,
    Func<string, string?>? getEnvironmentVariable = null,
    Func<IReadOnlyList<string>, CancellationToken, Task<ProcessAwsCredentialsResult>>? runProcess =
        null
) : IAwsCredentialsProvider
{
    private static readonly HttpClient SharedHttpClient = new();

    private readonly HttpClient httpClient = httpClient ?? SharedHttpClient;
    private readonly Func<string, string?> getEnvironmentVariable =
        getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = AwsSharedConfig.Load(
            getEnvironmentVariable,
            profileName,
            configPath,
            credentialsPath
        );
        var values = config.Profile(config.SelectedProfile);
        if (values.Count == 0)
        {
            throw new AwsCredentialsProviderException(
                nameof(ProfileAwsCredentialsProvider),
                $"AWS profile '{config.SelectedProfile}' was not found.",
                isNotConfigured: true
            );
        }
        return await ResolveAsync(config, config.SelectedProfile, values, [], cancellationToken)
            .ConfigureAwait(false);
    }

    private static TimeSpan? Duration(string name, Dictionary<string, string> values)
    {
        if (!values.TryGetValue("duration_seconds", out var text))
        {
            return null;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : throw Failure(
                $"AWS profile '{name}' has duration_seconds '{text}', which is not a number."
            );
    }

    private static bool TryGetStaticCredentials(
        Dictionary<string, string> values,
        out AwsCredentials credentials
    )
    {
        if (
            values.TryGetValue("aws_access_key_id", out var accessKeyId)
            && values.TryGetValue("aws_secret_access_key", out var secretAccessKey)
        )
        {
            values.TryGetValue("aws_session_token", out var sessionToken);
            credentials = new AwsCredentials(accessKeyId, secretAccessKey, sessionToken);
            return true;
        }
        credentials = null!;
        return false;
    }

    private static bool TryGetSsoSettings(
        AwsSharedConfig config,
        Dictionary<string, string> values,
        out SsoSettings settings
    )
    {
        var merged = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        if (
            values.TryGetValue("sso_session", out var session)
            && config.ConfigSection("sso-session " + session) is { } sessionValues
        )
        {
            foreach (var value in sessionValues)
            {
                merged[value.Key] = value.Value;
            }
        }
        if (
            merged.TryGetValue("sso_start_url", out var startUrl)
            && merged.TryGetValue("sso_region", out var region)
            && merged.TryGetValue("sso_account_id", out var accountId)
            && merged.TryGetValue("sso_role_name", out var roleName)
        )
        {
            settings = new SsoSettings(startUrl, region, accountId, roleName);
            return true;
        }
        settings = default;
        return false;
    }

    private static AwsCredentialsProviderException Failure(string message) =>
        new(nameof(ProfileAwsCredentialsProvider), message);

    private async ValueTask<AwsCredentials> ResolveAsync(
        AwsSharedConfig config,
        string name,
        Dictionary<string, string> values,
        List<string> visited,
        CancellationToken cancellationToken
    )
    {
        visited.Add(name);
        if (values.TryGetValue("role_arn", out var roleArn))
        {
            return await AssumeRoleAsync(config, name, values, roleArn, visited, cancellationToken)
                .ConfigureAwait(false);
        }

        if (TryGetSsoSettings(config, values, out var sso))
        {
            return await new SsoAwsCredentialsProvider(
                sso.AccountId,
                sso.RoleName,
                sso.Region,
                sso.StartUrl,
                ssoCacheDirectory ?? Path.Combine(config.AwsDirectory, "sso", "cache"),
                httpClient,
                timeProvider
            )
                .GetCredentialsAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (values.TryGetValue("credential_process", out var process))
        {
            return await new ProcessAwsCredentialsProvider(process, runProcess)
                .GetCredentialsAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (TryGetStaticCredentials(values, out var credentials))
        {
            return credentials;
        }

        throw Failure(
            $"AWS profile '{name}' has no role, IAM Identity Center configuration, credential_process, or static credentials."
        );
    }

    private async ValueTask<AwsCredentials> AssumeRoleAsync(
        AwsSharedConfig config,
        string name,
        Dictionary<string, string> values,
        string roleArn,
        List<string> visited,
        CancellationToken cancellationToken
    )
    {
        if (values.ContainsKey("mfa_serial"))
        {
            throw Failure(
                $"AWS profile '{name}' requires MFA (mfa_serial), which needs an interactive token code."
            );
        }

        values.TryGetValue("role_session_name", out var sessionName);
        var region = values.TryGetValue("region", out var profileRegion)
            ? profileRegion
            : AwsRegion.Resolve(getEnvironmentVariable, configPath);

        if (values.TryGetValue("web_identity_token_file", out var tokenFile))
        {
            return await new WebIdentityAwsCredentialsProvider(
                tokenFile,
                roleArn,
                sessionName,
                region,
                httpClient,
                timeProvider,
                getEnvironmentVariable
            )
                .GetCredentialsAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var source = await SourceCredentialsAsync(config, name, values, visited, cancellationToken)
            .ConfigureAwait(false);
        values.TryGetValue("external_id", out var externalId);
        return await AwsSts
            .AssumeRoleAsync(
                nameof(ProfileAwsCredentialsProvider),
                httpClient,
                timeProvider,
                region,
                source,
                roleArn,
                sessionName ?? AwsSts.DefaultSessionName(timeProvider),
                externalId,
                Duration(name, values),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async ValueTask<AwsCredentials> SourceCredentialsAsync(
        AwsSharedConfig config,
        string name,
        Dictionary<string, string> values,
        List<string> visited,
        CancellationToken cancellationToken
    )
    {
        if (values.TryGetValue("source_profile", out var sourceProfile))
        {
            // A profile that names itself as its source assumes the role with its own keys.
            if (string.Equals(sourceProfile, name, StringComparison.Ordinal))
            {
                return TryGetStaticCredentials(values, out var own)
                    ? own
                    : throw Failure(
                        $"AWS profile '{name}' is its own source_profile but has no static credentials."
                    );
            }
            if (visited.Contains(sourceProfile, StringComparer.Ordinal))
            {
                throw Failure(
                    $"AWS profiles form a source_profile cycle: {string.Join(" -> ", visited)} -> {sourceProfile}."
                );
            }
            var sourceValues = config.Profile(sourceProfile);
            if (sourceValues.Count == 0)
            {
                throw Failure(
                    $"AWS profile '{name}' names source_profile '{sourceProfile}', which was not found."
                );
            }
            return await ResolveAsync(
                    config,
                    sourceProfile,
                    sourceValues,
                    visited,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (values.TryGetValue("credential_source", out var credentialSource))
        {
            IAwsCredentialsProvider provider = credentialSource switch
            {
                "Environment" => new EnvironmentAwsCredentialsProvider(getEnvironmentVariable),
                "Ec2InstanceMetadata" => new InstanceMetadataAwsCredentialsProvider(
                    getEnvironmentVariable: getEnvironmentVariable
                ),
                "EcsContainer" => new ContainerAwsCredentialsProvider(
                    getEnvironmentVariable: getEnvironmentVariable
                ),
                _ => throw Failure(
                    $"AWS profile '{name}' has credential_source '{credentialSource}'; expected Environment, Ec2InstanceMetadata, or EcsContainer."
                ),
            };
            return await provider.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }

        throw Failure(
            $"AWS profile '{name}' has role_arn but no source_profile, credential_source, or web_identity_token_file."
        );
    }

    private readonly record struct SsoSettings(
        string StartUrl,
        string Region,
        string AccountId,
        string RoleName
    );
}
