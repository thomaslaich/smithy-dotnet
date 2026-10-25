namespace NSmithy.Aws;

/// <summary>The AWS region an application runs in, as the AWS SDKs resolve it.</summary>
public static class AwsRegion
{
    /// <summary>
    /// The first of <c>AWS_REGION</c>, <c>AWS_DEFAULT_REGION</c>, and the <c>region</c> of the
    /// selected profile in the shared <c>config</c> file, or null when none is set. Reads no
    /// network.
    /// </summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable. Default: the process environment.</param>
    /// <param name="configPath">The shared <c>config</c> file. Default: <c>AWS_CONFIG_FILE</c> or <c>~/.aws/config</c>.</param>
    public static string? Resolve(
        Func<string, string?>? getEnvironmentVariable = null,
        string? configPath = null
    )
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var region =
            NonEmpty(getEnvironmentVariable("AWS_REGION"))
            ?? NonEmpty(getEnvironmentVariable("AWS_DEFAULT_REGION"));
        if (region is not null)
        {
            return region;
        }

        try
        {
            var config = AwsSharedConfig.Load(getEnvironmentVariable, configPath: configPath);
            return config
                .Profile(config.SelectedProfile)
                .TryGetValue("region", out var profileRegion)
                ? NonEmpty(profileRegion)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
