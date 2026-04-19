using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Smithy.NET.CodeGeneration;
using Smithy.NET.CodeGeneration.CSharp;

namespace Smithy.NET.MSBuild;

public sealed class GenerateSmithyCode : Microsoft.Build.Utilities.Task
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    [Required]
    public string WorkingDirectory { get; set; } = string.Empty;

    [Required]
    public string BuildFile { get; set; } = string.Empty;

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    [Required]
    public string GeneratedOutputDirectory { get; set; } = string.Empty;

    public string? Projection { get; set; }

    public string? SmithyCliPath { get; set; }

    public string? BaseNamespace { get; set; }

    public string? GeneratedFileManifest { get; set; }

    public string? DependencyManifest { get; set; }

    public string? DependencyInputFile { get; set; }

    public ITaskItem[] SmithyModel { get; set; } = [];

    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];

    public override bool Execute()
    {
        try
        {
            ExecuteAsync().GetAwaiter().GetResult();
            return !Log.HasLoggedErrors;
        }
        catch (Exception exception) when (exception is SmithyException or IOException)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }

    private async System.Threading.Tasks.Task ExecuteAsync()
    {
        var outputDirectory = ResolveProjectPath(OutputDirectory);
        var generatedOutputDirectory = ResolveProjectPath(GeneratedOutputDirectory);
        var generatedFileManifest = NormalizeOptional(GeneratedFileManifest) is { } manifest
            ? ResolveProjectPath(manifest)
            : null;
        var dependencyManifest = NormalizeOptional(DependencyManifest) is { } dependencyManifestPath
            ? ResolveProjectPath(dependencyManifestPath)
            : null;
        var dependencyInputFile = NormalizeOptional(DependencyInputFile)
            is { } dependencyInputFilePath
            ? ResolveProjectPath(dependencyInputFilePath)
            : null;
        var buildFile = ResolveBuildFile(outputDirectory);
        var result = await new SmithyCli()
            .BuildAsync(
                new SmithyBuildOptions(
                    WorkingDirectory,
                    buildFile,
                    outputDirectory,
                    NormalizeOptional(Projection),
                    NormalizeOptional(SmithyCliPath)
                )
            )
            .ConfigureAwait(false);

        var files = new CSharpShapeGenerator().Generate(
            result.Model,
            new CSharpGenerationOptions(BaseNamespace: NormalizeOptional(BaseNamespace))
        );
        var generatedPaths = files
            .Select(file =>
                Path.GetFullPath(
                    Path.Combine(
                        generatedOutputDirectory,
                        file.Path.Replace('/', Path.DirectorySeparatorChar)
                    )
                )
            )
            .ToArray();
        var generatedPathSet = generatedPaths.ToHashSet(StringComparer.Ordinal);

        Directory.CreateDirectory(generatedOutputDirectory);
        DeleteStaleGeneratedFiles(
            generatedPathSet,
            generatedOutputDirectory,
            generatedFileManifest
        );

        var generatedFiles = new List<ITaskItem>(files.Count);

        foreach (var (file, destination) in files.Zip(generatedPaths))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, file.Contents);
            generatedFiles.Add(new TaskItem(destination));
        }

        WriteGeneratedFileManifest(generatedPaths, generatedFileManifest);
        var dependencyInputs = GetDependencyInputs(result, buildFile);
        WriteDependencyManifest(result, buildFile, dependencyInputs, dependencyManifest);
        WriteDependencyInputFile(dependencyInputs, dependencyInputFile);
        GeneratedFiles = generatedFiles.ToArray();
    }

    private string[] GetDependencyInputs(SmithyBuildResult result, string buildFile)
    {
        return
        [
            .. SmithyModel
                .Select(item => ResolveProjectPath(item.ItemSpec))
                .Concat(ReadBuildModelInputs(ResolveProjectPath(buildFile)))
                .Concat(ReadSmithySourceArtifacts(result.ModelPath))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private void WriteDependencyManifest(
        SmithyBuildResult result,
        string buildFile,
        string[] dependencyInputs,
        string? dependencyManifest
    )
    {
        if (string.IsNullOrWhiteSpace(dependencyManifest))
        {
            return;
        }

        var manifest = new SmithyDependencyManifest(
            ModelPath: Path.GetFullPath(result.ModelPath),
            BuildFile: ResolveProjectPath(buildFile),
            ConfiguredModelInputs:
            [
                .. SmithyModel
                    .Select(item => ResolveProjectPath(item.ItemSpec))
                    .Order(StringComparer.Ordinal),
            ],
            DependencyInputs: dependencyInputs,
            SmithySourceArtifacts: ReadSmithySourceArtifacts(result.ModelPath)
        );

        var manifestDirectory = Path.GetDirectoryName(dependencyManifest);
        if (!string.IsNullOrWhiteSpace(manifestDirectory))
        {
            Directory.CreateDirectory(manifestDirectory);
        }

        File.WriteAllText(
            dependencyManifest,
            JsonSerializer.Serialize(manifest, IndentedJsonOptions)
        );
    }

    private static void WriteDependencyInputFile(
        string[] dependencyInputs,
        string? dependencyInputFile
    )
    {
        if (string.IsNullOrWhiteSpace(dependencyInputFile))
        {
            return;
        }

        var manifestDirectory = Path.GetDirectoryName(dependencyInputFile);
        if (!string.IsNullOrWhiteSpace(manifestDirectory))
        {
            Directory.CreateDirectory(manifestDirectory);
        }

        File.WriteAllLines(dependencyInputFile, dependencyInputs);
    }

    private static string[] ReadBuildModelInputs(string buildFile)
    {
        if (!File.Exists(buildFile))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(buildFile);
            using var document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }
            );
            var buildFileDirectory =
                Path.GetDirectoryName(Path.GetFullPath(buildFile))
                ?? Directory.GetCurrentDirectory();
            return
            [
                .. ReadModelPathArray(document.RootElement, "sources", buildFileDirectory)
                    .Concat(ReadModelPathArray(document.RootElement, "imports", buildFileDirectory))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<string> ReadModelPathArray(
        JsonElement root,
        string propertyName,
        string buildFileDirectory
    )
    {
        if (
            !root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array
        )
        {
            yield break;
        }

        foreach (var value in property.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var configuredPath = value.GetString();
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                continue;
            }

            foreach (var path in ExpandModelInputPath(configuredPath, buildFileDirectory))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> ExpandModelInputPath(string path, string buildFileDirectory)
    {
        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(path) ? path : Path.Combine(buildFileDirectory, path)
        );
        if (File.Exists(fullPath))
        {
            yield return fullPath;
            yield break;
        }

        if (!Directory.Exists(fullPath))
        {
            yield break;
        }

        foreach (
            var file in Directory.EnumerateFiles(fullPath, "*.smithy", SearchOption.AllDirectories)
        )
        {
            yield return Path.GetFullPath(file);
        }

        foreach (
            var file in Directory.EnumerateFiles(fullPath, "*.json", SearchOption.AllDirectories)
        )
        {
            yield return Path.GetFullPath(file);
        }
    }

    private static string[] ReadSmithySourceArtifacts(string modelPath)
    {
        var projectionDirectory = Directory.GetParent(modelPath)?.Parent?.FullName;
        if (projectionDirectory is null)
        {
            return [];
        }

        var sourceDirectory = Path.Combine(projectionDirectory, "sources");
        var sourceManifest = Path.Combine(sourceDirectory, "manifest");
        if (!File.Exists(sourceManifest))
        {
            return [];
        }

        return
        [
            .. File.ReadLines(sourceManifest)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => Path.GetFullPath(Path.Combine(sourceDirectory, line.Trim())))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static void DeleteStaleGeneratedFiles(
        HashSet<string> generatedPaths,
        string generatedOutputDirectory,
        string? generatedFileManifest
    )
    {
        if (string.IsNullOrWhiteSpace(generatedFileManifest) || !File.Exists(generatedFileManifest))
        {
            return;
        }

        var outputDirectory = Path.GetFullPath(generatedOutputDirectory);
        foreach (var path in ReadGeneratedFileManifest(generatedFileManifest))
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsUnderDirectory(fullPath, outputDirectory) || generatedPaths.Contains(fullPath))
            {
                continue;
            }

            File.Delete(fullPath);
        }
    }

    private static string[] ReadGeneratedFileManifest(string generatedFileManifest)
    {
        if (string.IsNullOrWhiteSpace(generatedFileManifest))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(File.ReadAllText(generatedFileManifest))
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void WriteGeneratedFileManifest(
        string[] generatedPaths,
        string? generatedFileManifest
    )
    {
        if (string.IsNullOrWhiteSpace(generatedFileManifest))
        {
            return;
        }

        var manifestDirectory = Path.GetDirectoryName(generatedFileManifest);
        if (!string.IsNullOrWhiteSpace(manifestDirectory))
        {
            Directory.CreateDirectory(manifestDirectory);
        }

        File.WriteAllText(
            generatedFileManifest,
            JsonSerializer.Serialize(generatedPaths.Order(StringComparer.Ordinal))
        );
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath.Length > 0
            && relativePath != "."
            && !relativePath.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
    }

    private string ResolveBuildFile(string outputDirectory)
    {
        var buildFilePath = ResolveBuildFilePath();
        if (File.Exists(buildFilePath))
        {
            return BuildFile;
        }

        if (SmithyModel.Length == 0)
        {
            return BuildFile;
        }

        Directory.CreateDirectory(outputDirectory);
        var generatedBuildFile = Path.Combine(outputDirectory, "smithy-build.generated.json");
        using var stream = File.Create(generatedBuildFile);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("version", "1.0");
        writer.WritePropertyName("sources");
        writer.WriteStartArray();
        foreach (
            var source in SmithyModel
                .Select(item => ResolveProjectPath(item.ItemSpec))
                .Order(StringComparer.Ordinal)
        )
        {
            writer.WriteStringValue(source);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("projections");
        writer.WriteStartObject();
        writer.WriteStartObject(NormalizeOptional(Projection) ?? "source");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return generatedBuildFile;
    }

    private string ResolveBuildFilePath()
    {
        return Path.GetFullPath(
            Path.IsPathRooted(BuildFile) ? BuildFile : Path.Combine(WorkingDirectory, BuildFile)
        );
    }

    private string ResolveProjectPath(string path)
    {
        return Path.GetFullPath(
            Path.IsPathRooted(path) ? path : Path.Combine(WorkingDirectory, path)
        );
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record SmithyDependencyManifest(
        string ModelPath,
        string BuildFile,
        string[] ConfiguredModelInputs,
        string[] DependencyInputs,
        string[] SmithySourceArtifacts
    );
}
