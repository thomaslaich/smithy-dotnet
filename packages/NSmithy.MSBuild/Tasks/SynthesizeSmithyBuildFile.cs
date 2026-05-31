using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>
/// Synthesizes a smithy-build.json for a server/client project by reading the
/// contracts project's smithy-build.json, replacing the sources with the supplied
/// absolute paths, and injecting the smithy-csharp-codegen Maven dependency and
/// csharp-codegen plugin entry.
/// </summary>
public sealed class SynthesizeSmithyBuildFile : MsBuildTask
{
    /// <summary>Path to the contracts project's smithy-build.json.</summary>
    [Required]
    public string ContractsBuildFile { get; set; } = "";

    /// <summary>Absolute paths to the .smithy source files.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>Smithy service shape ID (e.g. com.example#MyService).</summary>
    [Required]
    public string Service { get; set; } = "";

    /// <summary>Base C# namespace for generated code. May be empty.</summary>
    public string BaseNamespace { get; set; } = "";

    /// <summary>Version of smithy-csharp-codegen Maven artifact to inject.</summary>
    [Required]
    public string CSharpCodegenVersion { get; set; } = "";

    /// <summary>Path where the synthesized smithy-build.json should be written.</summary>
    [Required]
    public string OutputFile { get; set; } = "";

    public override bool Execute()
    {
        string contractsJson;
        try
        {
            contractsJson = File.ReadAllText(ContractsBuildFile);
        }
        catch (Exception ex)
        {
            Log.LogError($"NSmithy: failed to read contracts smithy-build.json at '{ContractsBuildFile}': {ex.Message}");
            return false;
        }

        using var doc = JsonDocument.Parse(contractsJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;

        // Collect Maven repositories from contracts (fall back to defaults).
        var repos = new List<string>();
        if (root.TryGetProperty("maven", out var maven) &&
            maven.TryGetProperty("repositories", out var reposEl))
        {
            foreach (var repo in reposEl.EnumerateArray())
                if (repo.TryGetProperty("url", out var url))
                    repos.Add(url.GetString()!);
        }
        if (repos.Count == 0)
        {
            repos.Add("file://~/.m2/repository");
            repos.Add("https://repo.maven.apache.org/maven2/");
        }

        // Collect Maven dependencies from contracts, then append smithy-csharp-codegen.
        var deps = new List<string>();
        if (root.TryGetProperty("maven", out var maven2) &&
            maven2.TryGetProperty("dependencies", out var depsEl))
        {
            foreach (var dep in depsEl.EnumerateArray())
                deps.Add(dep.GetString()!);
        }
        var codegenDep = $"io.github.thomaslaich.nsmithy:smithy-csharp-codegen:{CSharpCodegenVersion}";
        if (!deps.Any(d => d.StartsWith("io.github.thomaslaich.nsmithy:smithy-csharp-codegen:")))
            deps.Add(codegenDep);

        // Collect suppressions from contracts.
        var suppressions = new List<(string Id, string Namespace)>();
        if (root.TryGetProperty("suppressions", out var suppressionsEl))
        {
            foreach (var s in suppressionsEl.EnumerateArray())
            {
                var id = s.TryGetProperty("id", out var idEl) ? idEl.GetString()! : "";
                var ns = s.TryGetProperty("namespace", out var nsEl) ? nsEl.GetString()! : "";
                suppressions.Add((id, ns));
            }
        }

        // Write synthesized JSON.
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("version", "1.0");

        if (suppressions.Count > 0)
        {
            writer.WritePropertyName("suppressions");
            writer.WriteStartArray();
            foreach (var (id, ns) in suppressions)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteString("namespace", ns);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WritePropertyName("sources");
        writer.WriteStartArray();
        foreach (var source in Sources)
            writer.WriteStringValue(source.GetMetadata("FullPath"));
        writer.WriteEndArray();

        writer.WritePropertyName("maven");
        writer.WriteStartObject();

        writer.WritePropertyName("repositories");
        writer.WriteStartArray();
        foreach (var repo in repos)
        {
            writer.WriteStartObject();
            writer.WriteString("url", repo);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("dependencies");
        writer.WriteStartArray();
        foreach (var dep in deps)
            writer.WriteStringValue(dep);
        writer.WriteEndArray();

        writer.WriteEndObject(); // maven

        writer.WritePropertyName("plugins");
        writer.WriteStartObject();
        writer.WritePropertyName("csharp-codegen");
        writer.WriteStartObject();
        writer.WriteString("service", Service);
        if (!string.IsNullOrEmpty(BaseNamespace))
            writer.WriteString("baseNamespace", BaseNamespace);
        writer.WriteEndObject();
        writer.WriteEndObject(); // plugins

        writer.WriteEndObject(); // root
        writer.Flush();

        var result = Encoding.UTF8.GetString(stream.ToArray());

        var outputDir = Path.GetDirectoryName(OutputFile);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        // Only write when content has changed to avoid unnecessary rebuilds.
        if (!File.Exists(OutputFile) || File.ReadAllText(OutputFile) != result)
            File.WriteAllText(OutputFile, result, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return true;
    }
}
