using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>
/// Writes the effective smithy-build.json that the Smithy CLI runs: the project's own build file
/// (or an empty one), the contracts' sources, dependencies, repositories, and suppressions, and
/// the plugin settings that MSBuild properties set. See designs/build-configuration.md.
/// </summary>
public sealed class WriteSmithyBuildFile : MsBuildTask
{
    private const string MavenCentral = "https://repo.maven.apache.org/maven2/";
    private const string CSharpPlugin = "csharp-codegen";
    private const string NSmithyGroup = "io.github.thomaslaich.nsmithy";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The project's own smithy-build.json. Empty or missing when it has none.</summary>
    public string ProjectBuildFile { get; set; } = "";

    /// <summary>Build files of the referenced contracts.</summary>
    public ITaskItem[] ContractsBuildFiles { get; set; } = [];

    /// <summary>Model files of the referenced contracts, added to the sources.</summary>
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>SmithyService. When empty, no plugin gets settings from properties.</summary>
    public string Service { get; set; } = "";

    /// <summary>SmithyGenerateClient; written as generateClient only when false.</summary>
    public bool GenerateClient { get; set; } = true;

    /// <summary>SmithyGenerateServer; written as generateServer only when false.</summary>
    public bool GenerateServer { get; set; } = true;

    /// <summary>SmithyGenerateDocs: adds the docgen plugin.</summary>
    public bool GenerateDocs { get; set; }

    /// <summary>SmithyOpenApiProtocol: adds the openapi plugin with this protocol.</summary>
    public string OpenApiProtocol { get; set; } = "";

    /// <summary>SmithyGrpc: adds the Protobuf plugin.</summary>
    public bool Grpc { get; set; }

    /// <summary>Name of the Protobuf plugin.</summary>
    public string ProtoPlugin { get; set; } = "proto-codegen";

    /// <summary>The projection that receives the settings from properties.</summary>
    public string Projection { get; set; } = "source";

    /// <summary>Version of NSmithy's plugin artifacts.</summary>
    [Required]
    public string CSharpCodegenVersion { get; set; } = "";

    /// <summary>Version of the Smithy docgen and openapi artifacts.</summary>
    [Required]
    public string SmithyVersion { get; set; } = "";

    /// <summary>Where the effective build file is written.</summary>
    [Required]
    public string OutputFile { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var root = ReadProjectBuildFile();
            var dependencies = new DependencySet();
            var repositories = new List<JsonObject>();

            CollectMaven(root, ProjectBuildFileName, dependencies, repositories);
            AddContracts(root, dependencies, repositories);
            if (!AddPropertySettings(root))
                return false;
            AddPluginArtifacts(root, dependencies);
            if (dependencies.Conflict is { } conflict)
            {
                LogError("NSMITHYDEPENDENCY", conflict);
                return false;
            }
            WriteMaven(root, dependencies, repositories);
            Write(root);
            return !Log.HasLoggedErrors;
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or InvalidOperationException)
        {
            Log.LogError(
                $"NSmithy: could not write the effective smithy-build.json: {exception.Message}"
            );
            return false;
        }
    }

    private bool HasProjectBuildFile =>
        !string.IsNullOrEmpty(ProjectBuildFile) && File.Exists(ProjectBuildFile);

    private string ProjectBuildFileName =>
        HasProjectBuildFile ? ProjectBuildFile : "smithy-build.json";

    private JsonObject ReadProjectBuildFile()
    {
        if (!HasProjectBuildFile)
            return new JsonObject { ["version"] = "1.0" };

        var root = ParseObject(ProjectBuildFile);
        var directory = Path.GetDirectoryName(Path.GetFullPath(ProjectBuildFile))!;
        ResolvePaths(root, "sources", directory);
        ResolvePaths(root, "imports", directory);
        if (root["projections"] is JsonObject projections)
            foreach (var (_, projection) in projections)
                if (projection is JsonObject projectionObject)
                    ResolvePaths(projectionObject, "imports", directory);
        return root;
    }

    private static JsonObject ParseObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject
        ?? throw new JsonException($"{path} is not a JSON object.");

    private static void ResolvePaths(JsonObject owner, string key, string directory)
    {
        if (owner[key] is not JsonArray paths)
            return;
        for (var i = 0; i < paths.Count; i++)
            if (paths[i]?.GetValue<string>() is { } path)
                paths[i] = Path.GetFullPath(path, directory);
    }

    private static void CollectMaven(
        JsonObject buildFile,
        string origin,
        DependencySet dependencies,
        List<JsonObject> repositories
    )
    {
        if (buildFile["maven"] is not JsonObject maven)
            return;
        if (maven["dependencies"] is JsonArray deps)
            foreach (var dep in deps)
                if (dep?.GetValue<string>() is { } coordinate)
                    dependencies.Add(coordinate, origin);
        // Whole repository objects, so id, httpCredentials, proxyHost, and proxyCredentials
        // survive; the first entry for a URL wins.
        if (maven["repositories"] is JsonArray repos)
            foreach (var repo in repos)
                if (
                    repo is JsonObject repository
                    && repository["url"]?.GetValue<string>() is { } url
                    && !repositories.Exists(known => known["url"]?.GetValue<string>() == url)
                )
                    repositories.Add(repository.DeepClone().AsObject());
    }

    private void AddContracts(
        JsonObject root,
        DependencySet dependencies,
        List<JsonObject> repositories
    )
    {
        foreach (var item in ContractsBuildFiles)
        {
            var path = item.GetMetadata("FullPath");
            var contract = ParseObject(path);
            CollectMaven(contract, path, dependencies, repositories);
            if (contract["suppressions"] is JsonArray suppressions)
            {
                var target = root["suppressions"] as JsonArray ?? [];
                root["suppressions"] = target;
                foreach (var suppression in suppressions)
                    target.Add(suppression?.DeepClone());
            }
        }

        if (Sources.Length == 0)
            return;
        var sources = root["sources"] as JsonArray ?? [];
        root["sources"] = sources;
        foreach (var source in Sources)
            sources.Add(source.GetMetadata("FullPath"));
    }

    private sealed record PropertySettings(
        string Plugin,
        string Property,
        JsonObject Owned,
        JsonObject Defaults
    );

    // The settings that properties set, per plugin. Owned keys fail the build when a build file
    // sets them too; defaults apply only when it does not.
    private bool AddPropertySettings(JsonObject root)
    {
        if (string.IsNullOrEmpty(Service))
        {
            foreach (
                var (enabled, property) in new[]
                {
                    (GenerateDocs, "SmithyGenerateDocs"),
                    (!string.IsNullOrEmpty(OpenApiProtocol), "SmithyOpenApiProtocol"),
                    (Grpc, "SmithyGrpc"),
                }
            )
                if (enabled)
                    LogError(
                        "NSMITHYSERVICE",
                        $"NSmithy: {property} requires SmithyService, which names the service its plugin generates for."
                    );
            return !Log.HasLoggedErrors;
        }

        var projectionPlugins = ProjectionPlugins(root);
        if (projectionPlugins is null)
            return false;
        var topLevel = root["plugins"] as JsonObject;

        foreach (var plugin in EnabledPlugins())
        {
            // The settings Smithy would use for the projection: its own when it defines the
            // plugin, which replace the top-level ones entirely, or else the top-level ones. The
            // top-level entry stays for the other projections that inherit it.
            var settings = (projectionPlugins[plugin.Plugin] ?? topLevel?[plugin.Plugin])
                is JsonObject inherited
                ? inherited.DeepClone().AsObject()
                : new JsonObject();

            foreach (var (key, value) in plugin.Owned)
            {
                if (settings.ContainsKey(key))
                {
                    LogError(
                        "NSMITHYPLUGIN",
                        $"NSmithy: {ProjectBuildFileName} sets \"{key}\" for the {plugin.Plugin} plugin, "
                            + $"which {PropertyFor(plugin, key)} also sets. Remove one of them."
                    );
                    continue;
                }
                settings[key] = value?.DeepClone();
            }
            foreach (var (key, value) in plugin.Defaults)
                if (!settings.ContainsKey(key))
                    settings[key] = value?.DeepClone();
            if (
                plugin.Plugin == ProtoPlugin
                && !settings.ContainsKey("baseNamespace")
                && projectionPlugins[CSharpPlugin]?["baseNamespace"]?.GetValue<string>()
                    is { } baseNamespace
            )
                settings["baseNamespace"] = baseNamespace;

            projectionPlugins[plugin.Plugin] = settings;
        }
        return !Log.HasLoggedErrors;
    }

    private IEnumerable<PropertySettings> EnabledPlugins()
    {
        var csharp = new JsonObject { ["service"] = Service };
        if (!GenerateClient)
            csharp["generateClient"] = false;
        if (!GenerateServer)
            csharp["generateServer"] = false;
        yield return new PropertySettings(CSharpPlugin, "SmithyService", csharp, []);

        if (GenerateDocs)
            yield return new PropertySettings(
                "docgen",
                "SmithyGenerateDocs",
                new JsonObject { ["service"] = Service },
                []
            );
        if (!string.IsNullOrEmpty(OpenApiProtocol))
            yield return new PropertySettings(
                "openapi",
                "SmithyOpenApiProtocol",
                new JsonObject { ["service"] = Service, ["protocol"] = OpenApiProtocol },
                new JsonObject { ["useIntegerType"] = true }
            );
        if (Grpc)
            yield return new PropertySettings(
                ProtoPlugin,
                "SmithyGrpc",
                new JsonObject { ["service"] = Service },
                new JsonObject
                {
                    ["fileOptions"] = new JsonObject
                    {
                        ["csharp_namespace"] = new JsonObject
                        {
                            ["suffix"] = "Grpc",
                            ["case"] = "pascal",
                        },
                    },
                }
            );
    }

    private static string PropertyFor(PropertySettings plugin, string key) =>
        (plugin.Plugin, key) switch
        {
            (CSharpPlugin, "generateClient") => "SmithyGenerateClient",
            (CSharpPlugin, "generateServer") => "SmithyGenerateServer",
            ("openapi", "protocol") => "SmithyOpenApiProtocol",
            (_, "service") => "SmithyService",
            _ => plugin.Property,
        };

    private JsonObject? ProjectionPlugins(JsonObject root)
    {
        var projections = root["projections"] as JsonObject;
        if (projections?[Projection] is not JsonObject projection)
        {
            if (Projection != "source")
            {
                LogError(
                    "NSMITHYPROJECTION",
                    $"NSmithy: SmithyProjection is '{Projection}', but {ProjectBuildFileName} defines no such projection."
                );
                return null;
            }
            projections ??= [];
            root["projections"] = projections;
            projection = [];
            projections[Projection] = projection;
        }
        var plugins = projection["plugins"] as JsonObject ?? [];
        projection["plugins"] = plugins;
        return plugins;
    }

    private void AddPluginArtifacts(JsonObject root, DependencySet dependencies)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (root["plugins"] is JsonObject topLevel)
            foreach (var (name, _) in topLevel)
                names.Add(name);
        if (root["projections"] is JsonObject projections)
            foreach (var (_, projection) in projections)
                if (projection?["plugins"] is JsonObject plugins)
                    foreach (var (name, _) in plugins)
                        names.Add(name);

        if (names.Contains(CSharpPlugin))
        {
            dependencies.Add(
                $"{NSmithyGroup}:smithy-csharp-codegen:{CSharpCodegenVersion}",
                "NSmithy"
            );
            dependencies.Add(
                $"{NSmithyGroup}:smithy-csharp-aws-codegen:{CSharpCodegenVersion}",
                "NSmithy"
            );
        }
        if (names.Contains(ProtoPlugin))
            dependencies.Add(
                $"{NSmithyGroup}:smithy-proto-codegen:{CSharpCodegenVersion}",
                "NSmithy"
            );
        if (names.Contains("docgen"))
            dependencies.Add($"software.amazon.smithy:smithy-docgen:{SmithyVersion}", "NSmithy");
        if (names.Contains("openapi"))
            dependencies.Add($"software.amazon.smithy:smithy-openapi:{SmithyVersion}", "NSmithy");
    }

    private static void WriteMaven(
        JsonObject root,
        DependencySet dependencies,
        List<JsonObject> repositories
    )
    {
        var maven = root["maven"] as JsonObject ?? [];
        root["maven"] = maven;
        if (repositories.Count == 0)
            repositories.Add(new JsonObject { ["url"] = MavenCentral });
        var repos = new JsonArray();
        foreach (var repository in repositories)
            repos.Add(repository);
        maven["repositories"] = repos;
        var deps = new JsonArray();
        foreach (var coordinate in dependencies.Coordinates)
            deps.Add(coordinate);
        maven["dependencies"] = deps;
    }

    private void Write(JsonObject root)
    {
        var result = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var directory = Path.GetDirectoryName(OutputFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        // Only write when the content changes, so an unchanged project skips generation.
        if (!File.Exists(OutputFile) || File.ReadAllText(OutputFile) != result)
            File.WriteAllText(
                OutputFile,
                result,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
    }

    private void LogError(string code, string message) =>
        Log.LogError(
            subcategory: null,
            errorCode: code,
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: message
        );

    /// <summary>Maven coordinates combined by group and artifact, in first-seen order.</summary>
    private sealed class DependencySet
    {
        private readonly Dictionary<string, (string Coordinate, string Origin)> _byArtifact = new(
            StringComparer.Ordinal
        );
        private readonly List<string> _order = [];

        public string? Conflict { get; private set; }

        public IEnumerable<string> Coordinates =>
            _order.Select(artifact => _byArtifact[artifact].Coordinate);

        public void Add(string coordinate, string origin)
        {
            var lastColon = coordinate.LastIndexOf(':');
            var artifact = lastColon > 0 ? coordinate[..lastColon] : coordinate;
            if (_byArtifact.TryGetValue(artifact, out var existing))
            {
                if (existing.Coordinate != coordinate && Conflict is null)
                    Conflict =
                        $"NSmithy: {existing.Origin} depends on {existing.Coordinate} and {origin} on "
                        + $"{coordinate}. Use one version of {artifact}.";
                return;
            }
            _byArtifact[artifact] = (coordinate, origin);
            _order.Add(artifact);
        }
    }
}
