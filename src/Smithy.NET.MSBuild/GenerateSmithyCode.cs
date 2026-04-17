using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Smithy.NET.CodeGeneration;
using Smithy.NET.CodeGeneration.CSharp;

namespace Smithy.NET.MSBuild;

public sealed class GenerateSmithyCode : Microsoft.Build.Utilities.Task
{
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
        var buildFile = ResolveBuildFile();
        var result = await new SmithyCli()
            .BuildAsync(
                new SmithyBuildOptions(
                    WorkingDirectory,
                    buildFile,
                    OutputDirectory,
                    NormalizeOptional(Projection),
                    NormalizeOptional(SmithyCliPath)
                )
            )
            .ConfigureAwait(false);

        Directory.CreateDirectory(GeneratedOutputDirectory);
        var files = new CSharpShapeGenerator()
            .Generate(
                result.Model,
                new CSharpGenerationOptions(BaseNamespace: NormalizeOptional(BaseNamespace))
            );
        var generatedFiles = new List<ITaskItem>(files.Count);

        foreach (var file in files)
        {
            var destination = Path.Combine(
                GeneratedOutputDirectory,
                file.Path.Replace('/', Path.DirectorySeparatorChar)
            );
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, file.Contents);
            generatedFiles.Add(new TaskItem(destination));
        }

        GeneratedFiles = generatedFiles.ToArray();
    }

    private string ResolveBuildFile()
    {
        var buildFilePath = Path.IsPathRooted(BuildFile)
            ? BuildFile
            : Path.Combine(WorkingDirectory, BuildFile);
        if (File.Exists(buildFilePath))
        {
            return BuildFile;
        }

        if (SmithyModel.Length == 0)
        {
            return BuildFile;
        }

        Directory.CreateDirectory(OutputDirectory);
        var generatedBuildFile = Path.Combine(OutputDirectory, "smithy-build.generated.json");
        using var stream = File.Create(generatedBuildFile);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("version", "1.0");
        writer.WritePropertyName("sources");
        writer.WriteStartArray();
        foreach (
            var source in SmithyModel.Select(item => item.ItemSpec).Order(StringComparer.Ordinal)
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

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
