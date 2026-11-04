using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>
/// Lists the model files of a project that publishes its model (SmithyPublish=true): the files
/// that its smithy-build.json names in <c>sources</c> and <c>imports</c>, with directories
/// expanded to the <c>.smithy</c> and <c>.json</c> files Smithy loads from them. These are the
/// same files the project generates its own shapes from, so the model it publishes, to project
/// references and in its package, is the model its generated types come from.
/// </summary>
public sealed class ResolveSmithySources : MsBuildTask
{
    private static readonly string[] ModelExtensions = [".smithy", ".json"];

    /// <summary>The project's smithy-build.json.</summary>
    [Required]
    public string BuildFile { get; set; } = "";

    /// <summary>
    /// The project directory. A file inside it gets the <c>PackagePath</c> metadata, its path
    /// under <c>build/smithy/</c> in the package; one outside it gets none.
    /// </summary>
    [Required]
    public string ProjectDirectory { get; set; } = "";

    /// <summary>The model files, with full paths.</summary>
    [Output]
    public ITaskItem[] Sources { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(BuildFile))!;
            var project = Path.GetFullPath(ProjectDirectory);
            var files = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var entry in SmithyBuildFiles.OwnSources(SmithyBuildFiles.Parse(BuildFile)))
            {
                var path = Path.GetFullPath(entry, directory);
                if (Directory.Exists(path))
                    files.UnionWith(
                        Directory
                            .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                            .Where(file =>
                                ModelExtensions.Contains(
                                    Path.GetExtension(file),
                                    StringComparer.OrdinalIgnoreCase
                                )
                            )
                    );
                else if (File.Exists(path))
                    files.Add(path);
                else
                    Log.LogError(
                        subcategory: null,
                        errorCode: "NSMITHYSOURCE",
                        helpKeyword: null,
                        file: BuildFile,
                        lineNumber: 0,
                        columnNumber: 0,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: $"NSmithy: {BuildFile} lists the model source \"{entry}\", which does not exist."
                    );
            }
            Sources = files.Select(file => Item(file, project)).ToArray();
            return !Log.HasLoggedErrors;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            Log.LogError(
                $"NSmithy: could not read the model sources of {BuildFile}: {exception.Message}"
            );
            return false;
        }
    }

    private static TaskItem Item(string file, string project)
    {
        var item = new TaskItem(file);
        var relative = Path.GetRelativePath(project, file);
        if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            item.SetMetadata("PackagePath", "build/smithy/" + relative.Replace('\\', '/'));
        return item;
    }
}
