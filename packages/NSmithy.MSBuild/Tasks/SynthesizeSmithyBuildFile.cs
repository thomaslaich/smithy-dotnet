using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>
/// Synthesizes a smithy-build.json for an application from local or referenced
/// Smithy sources. When a contracts build file is supplied, its Maven settings
/// and suppressions are inherited.
/// </summary>
public sealed class SynthesizeSmithyBuildFile : MsBuildTask
{
    /// <summary>
    /// Optional legacy path to one contracts project's smithy-build.json.
    /// Prefer <see cref="ContractsBuildFiles" /> for new callers.
    /// </summary>
    public string ContractsBuildFile { get; set; } = "";

    /// <summary>Optional paths to contracts projects' smithy-build.json files.</summary>
    public ITaskItem[] ContractsBuildFiles { get; set; } = [];

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

    /// <summary>Additional Maven artifacts contributed by codegen extension packages.</summary>
    public ITaskItem[] AdditionalMavenDependencies { get; set; } = [];

    /// <summary>
    /// Additional Smithy build plugins contributed by extension packages. The item identity is the
    /// plugin name; optional Service and SettingsJson metadata populate its configuration.
    /// </summary>
    public ITaskItem[] AdditionalPlugins { get; set; } = [];

    /// <summary>
    /// Smithy SDK version used when injecting optional plugin dependencies.
    /// Should match the bundled Smithy CLI version.
    /// </summary>
    [Required]
    public string SmithyVersion { get; set; } = "";

    /// <summary>When true, injects the smithy-docgen Maven dep and docgen plugin entry.</summary>
    public bool GenerateDocs { get; set; }

    /// <summary>
    /// Whether the csharp-codegen plugin emits the client surface. When false, sets
    /// generateClient:false so the plugin never writes the client half (rather than writing it and
    /// excluding it from compilation). Default true.
    /// </summary>
    public bool GenerateClient { get; set; } = true;

    /// <summary>
    /// Whether the csharp-codegen plugin emits the server surface. When false, sets
    /// generateServer:false so the plugin never writes the server half. Default true.
    /// </summary>
    public bool GenerateServer { get; set; } = true;

    /// <summary>
    /// When true, sets generateDependencyInjection on the csharp-codegen plugin so the
    /// IHttpClientFactory registration extension is generated.
    /// </summary>
    public bool GenerateDependencyInjection { get; set; }

    /// <summary>
    /// When true, sets generateFakes on the csharp-codegen plugin so the fakes are generated:
    /// the fake handler ({Service}.Fakes.Server.g.cs) with the server surface and the fake
    /// client ({Service}.Fakes.Client.g.cs) with the client surface.
    /// </summary>
    public bool GenerateFakes { get; set; }

    /// <summary>
    /// When non-empty, injects the smithy-openapi Maven dep and openapi plugin entry
    /// using this value as the protocol (e.g. aws.protocols#restJson1).
    /// </summary>
    public string OpenApiProtocol { get; set; } = "";

    /// <summary>
    /// When true, injects the smithy-proto-codegen Maven dep and the proto-codegen
    /// plugin entry so a .proto file is generated for gRPC services.
    /// </summary>
    public bool Grpc { get; set; }

    /// <summary>Name of the proto codegen plugin (defaults to proto-codegen).</summary>
    public string ProtoPlugin { get; set; } = "proto-codegen";

    /// <summary>Path where the synthesized smithy-build.json should be written.</summary>
    [Required]
    public string OutputFile { get; set; } = "";

    public override bool Execute()
    {
        var contractsBuildFiles = ContractsBuildFiles
            .Select(item => item.GetMetadata("FullPath"))
            .Append(ContractsBuildFile)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var contractRoots = new List<JsonElement>();
        foreach (var contractsBuildFile in contractsBuildFiles)
        {
            try
            {
                using var document = JsonDocument.Parse(
                    File.ReadAllText(contractsBuildFile),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }
                );
                contractRoots.Add(document.RootElement.Clone());
            }
            catch (Exception ex)
            {
                Log.LogError(
                    $"NSmithy: failed to read contracts smithy-build.json at '{contractsBuildFile}': {ex.Message}"
                );
                return false;
            }
        }

        // Collect Maven repositories from contracts (fall back to defaults).
        var repos = new List<string>();
        foreach (var root in contractRoots)
        {
            if (
                root.TryGetProperty("maven", out var maven)
                && maven.TryGetProperty("repositories", out var reposElement)
            )
            {
                foreach (var repo in reposElement.EnumerateArray())
                    if (
                        repo.TryGetProperty("url", out var url)
                        && url.GetString() is { } value
                        && !repos.Contains(value)
                    )
                        repos.Add(value);
            }
        }
        if (repos.Count == 0)
            repos.Add("https://repo.maven.apache.org/maven2/");

        // Collect Maven dependencies from contracts, then append smithy-csharp-codegen.
        var deps = new List<string>();
        foreach (var root in contractRoots)
        {
            if (
                root.TryGetProperty("maven", out var maven)
                && maven.TryGetProperty("dependencies", out var dependenciesElement)
            )
            {
                foreach (var dependency in dependenciesElement.EnumerateArray())
                    if (dependency.GetString() is { } value && !deps.Contains(value))
                        deps.Add(value);
            }
        }
        var codegenDep =
            $"io.github.thomaslaich.nsmithy:smithy-csharp-codegen:{CSharpCodegenVersion}";
        if (
            !deps.Any(d =>
                d.StartsWith(
                    "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:",
                    StringComparison.Ordinal
                )
            )
        )
            deps.Add(codegenDep);

        foreach (var dependency in AdditionalMavenDependencies)
        {
            var coordinate = dependency.ItemSpec;
            if (!string.IsNullOrWhiteSpace(coordinate) && !deps.Contains(coordinate))
                deps.Add(coordinate);
        }

        if (GenerateDocs)
            deps.Add($"software.amazon.smithy:smithy-docgen:{SmithyVersion}");

        if (!string.IsNullOrEmpty(OpenApiProtocol))
            deps.Add($"software.amazon.smithy:smithy-openapi:{SmithyVersion}");

        // smithy-proto-codegen is released in lockstep with smithy-csharp-codegen,
        // so it shares the same version. Skip if the contracts already declare it.
        if (
            Grpc
            && !deps.Any(d =>
                d.StartsWith(
                    "io.github.thomaslaich.nsmithy:smithy-proto-codegen:",
                    StringComparison.Ordinal
                )
            )
        )
            deps.Add($"io.github.thomaslaich.nsmithy:smithy-proto-codegen:{CSharpCodegenVersion}");

        // Collect suppressions from contracts.
        var suppressions = new List<(string Id, string Namespace)>();
        foreach (var root in contractRoots)
        {
            if (!root.TryGetProperty("suppressions", out var suppressionsElement))
                continue;

            foreach (var suppression in suppressionsElement.EnumerateArray())
            {
                var id = suppression.TryGetProperty("id", out var idElement)
                    ? idElement.GetString()!
                    : "";
                var ns = suppression.TryGetProperty("namespace", out var namespaceElement)
                    ? namespaceElement.GetString()!
                    : "";
                if (!suppressions.Contains((id, ns)))
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
        // The plugin defaults both halves to true, so only the opt-outs are written.
        if (!GenerateClient)
            writer.WriteBoolean("generateClient", false);
        if (!GenerateServer)
            writer.WriteBoolean("generateServer", false);
        if (GenerateDependencyInjection)
            writer.WriteBoolean("generateDependencyInjection", true);
        if (GenerateFakes)
            writer.WriteBoolean("generateFakes", true);
        writer.WriteEndObject();

        if (GenerateDocs)
        {
            writer.WritePropertyName("docgen");
            writer.WriteStartObject();
            writer.WriteString("service", Service);
            writer.WriteEndObject();
        }

        if (!string.IsNullOrEmpty(OpenApiProtocol))
        {
            writer.WritePropertyName("openapi");
            writer.WriteStartObject();
            writer.WriteString("service", Service);
            writer.WriteString("protocol", OpenApiProtocol);
            writer.WriteEndObject();
        }

        foreach (var plugin in AdditionalPlugins)
        {
            if (string.IsNullOrWhiteSpace(plugin.ItemSpec))
                continue;

            writer.WritePropertyName(plugin.ItemSpec);
            writer.WriteStartObject();

            var pluginService = plugin.GetMetadata("Service");
            if (!string.IsNullOrWhiteSpace(pluginService))
                writer.WriteString("service", pluginService);

            var settingsJson = plugin.GetMetadata("SettingsJson");
            if (!string.IsNullOrWhiteSpace(settingsJson))
            {
                using var settings = JsonDocument.Parse(settingsJson);
                if (settings.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException(
                        $"Smithy plugin '{plugin.ItemSpec}' SettingsJson must be a JSON object."
                    );
                foreach (var property in settings.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("service") && !string.IsNullOrWhiteSpace(pluginService))
                        continue;
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        if (Grpc)
        {
            writer.WritePropertyName(ProtoPlugin);
            writer.WriteStartObject();
            writer.WriteString("service", Service);
            if (!string.IsNullOrEmpty(BaseNamespace))
                writer.WriteString("baseNamespace", BaseNamespace);
            writer.WritePropertyName("fileOptions");
            writer.WriteStartObject();
            writer.WritePropertyName("csharp_namespace");
            writer.WriteStartObject();
            writer.WriteString("suffix", "Grpc");
            writer.WriteString("case", "pascal");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject(); // plugins

        writer.WriteEndObject(); // root
        writer.Flush();

        var result = Encoding.UTF8.GetString(stream.ToArray());

        var outputDir = Path.GetDirectoryName(OutputFile);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        // Only write when content has changed to avoid unnecessary rebuilds.
        if (!File.Exists(OutputFile) || File.ReadAllText(OutputFile) != result)
            File.WriteAllText(
                OutputFile,
                result,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );

        return true;
    }
}
