using System.Xml.Linq;

namespace DotnetNsmithy;

/// <summary>
/// Reads NSmithy-specific properties from an SDK-style .csproj without invoking MSBuild.
/// Only literal property values are supported — no imports or conditions are evaluated.
/// </summary>
internal sealed class CsprojReader
{
    private readonly Dictionary<string, string> _props;

    private CsprojReader(Dictionary<string, string> props) => _props = props;

    public string? GroupId => Get("SmithyMavenGroupId");
    public string? ArtifactId => Get("SmithyMavenArtifactId");
    public string? Sources => Get("SmithySources");

    public string? Version
    {
        get
        {
            // Honour an explicit <Version> override first.
            var version = Get("Version");
            if (version is not null)
                return version;

            var prefix = Get("VersionPrefix");
            if (prefix is null)
                return null;

            var suffix = Get("VersionSuffix");
            return string.IsNullOrEmpty(suffix) ? prefix : $"{prefix}-{suffix}";
        }
    }

    private string? Get(string name) => _props.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Locates and parses a .csproj. Returns null if no project file is found.
    /// </summary>
    public static CsprojReader? TryLoad(FileInfo? projectFile, DirectoryInfo workingDir)
    {
        var file = projectFile ?? FindCsproj(workingDir);
        if (file is null || !file.Exists)
            return null;

        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = XDocument.Load(file.FullName);

        foreach (var pg in doc.Descendants("PropertyGroup"))
        {
            foreach (var el in pg.Elements())
            {
                var value = el.Value.Trim();
                if (!string.IsNullOrEmpty(value))
                    props[el.Name.LocalName] = value;
            }
        }

        return new CsprojReader(props);
    }

    private static FileInfo? FindCsproj(DirectoryInfo dir)
    {
        var files = dir.GetFiles("*.csproj");
        return files.Length == 1 ? files[0] : null;
    }
}
