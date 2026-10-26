namespace NSmithy.Aws;

/// <summary>
/// The AWS shared <c>config</c> and <c>credentials</c> files, and the profile the environment
/// selects in them.
/// </summary>
internal sealed class AwsSharedConfig
{
    private readonly Dictionary<string, Dictionary<string, string>> config;
    private readonly Dictionary<string, Dictionary<string, string>> credentials;

    private AwsSharedConfig(
        string selectedProfile,
        string awsDirectory,
        Dictionary<string, Dictionary<string, string>> config,
        Dictionary<string, Dictionary<string, string>> credentials
    )
    {
        SelectedProfile = selectedProfile;
        AwsDirectory = awsDirectory;
        this.config = config;
        this.credentials = credentials;
    }

    /// <summary>The profile <c>AWS_PROFILE</c> names, or <c>default</c>.</summary>
    public string SelectedProfile { get; }

    /// <summary>The <c>~/.aws</c> directory.</summary>
    public string AwsDirectory { get; }

    /// <summary>Whether either file has any section.</summary>
    public bool IsEmpty => config.Count == 0 && credentials.Count == 0;

    public static AwsSharedConfig Load(
        Func<string, string?> getEnvironmentVariable,
        string? profileName = null,
        string? configPath = null,
        string? credentialsPath = null
    )
    {
        var awsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aws"
        );
        var selectedProfile =
            profileName
            ?? getEnvironmentVariable("AWS_PROFILE")
            ?? getEnvironmentVariable("AWS_DEFAULT_PROFILE")
            ?? "default";
        return new AwsSharedConfig(
            selectedProfile,
            awsDirectory,
            ReadIniFile(
                configPath
                    ?? getEnvironmentVariable("AWS_CONFIG_FILE")
                    ?? Path.Combine(awsDirectory, "config")
            ),
            ReadIniFile(
                credentialsPath
                    ?? getEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE")
                    ?? Path.Combine(awsDirectory, "credentials")
            )
        );
    }

    /// <summary>
    /// A profile's settings: its <c>config</c> section, overridden by its <c>credentials</c>
    /// section. Empty when neither file has the profile.
    /// </summary>
    public Dictionary<string, string> Profile(string name)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var configSection = string.Equals(name, "default", StringComparison.Ordinal)
            ? name
            : "profile " + name;
        Merge(values, config, configSection);
        Merge(values, credentials, name);
        return values;
    }

    /// <summary>A section of the <c>config</c> file by its full name, such as <c>sso-session x</c>.</summary>
    public IReadOnlyDictionary<string, string>? ConfigSection(string name) =>
        config.TryGetValue(name, out var values) ? values : null;

    private static void Merge(
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
                if (!sections.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    sections[name] = current;
                }
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
}
