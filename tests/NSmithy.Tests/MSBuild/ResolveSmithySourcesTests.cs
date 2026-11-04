using NSmithy.Contracts;

namespace NSmithy.Tests.MSBuild;

public sealed class ResolveSmithySourcesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nsmithy-sources-{Guid.NewGuid():N}"
    );

    public ResolveSmithySourcesTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ListsTheModelFilesTheBuildFileNames()
    {
        WriteFile("model/orders.smithy");
        WriteFile("model/nested/ast.json");
        WriteFile("model/README.md");
        WriteFile("shared/money.smithy");
        WriteFile("shared/unlisted.smithy");
        var buildFile = WriteFile(
            "smithy-build.json",
            """
            {
              // Smithy allows comments and trailing commas.
              "version": "1.0",
              "sources": ["model"],
              "imports": ["shared/money.smithy",],
            }
            """
        );

        var (sources, errors) = Resolve(buildFile);

        Assert.Empty(errors);
        Assert.Equal(
            [
                ("model/nested/ast.json", "build/smithy/model/nested/ast.json"),
                ("model/orders.smithy", "build/smithy/model/orders.smithy"),
                ("shared/money.smithy", "build/smithy/shared/money.smithy"),
            ],
            sources
        );
    }

    [Fact]
    public void LeavesSourcesOutsideTheProjectWithoutPackagePath()
    {
        var outside = Path.Combine(
            Path.GetTempPath(),
            $"nsmithy-outside-{Guid.NewGuid():N}.smithy"
        );
        File.WriteAllText(outside, "");
        try
        {
            var buildFile = WriteFile(
                "smithy-build.json",
                $$"""{ "version": "1.0", "sources": ["{{outside.Replace("\\", "\\\\")}}"] }"""
            );

            var task = new ResolveSmithySources
            {
                BuildEngine = new RecordingBuildEngine(),
                BuildFile = buildFile,
                ProjectDirectory = _directory,
            };

            Assert.True(task.Execute());
            var source = Assert.Single(task.Sources);
            Assert.Equal(outside, source.ItemSpec);
            Assert.Equal("", source.GetMetadata("PackagePath"));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void FailsForASourceThatDoesNotExist()
    {
        var buildFile = WriteFile(
            "smithy-build.json",
            """{ "version": "1.0", "sources": ["missing"] }"""
        );

        var (_, errors) = Resolve(buildFile);

        var error = Assert.Single(errors);
        Assert.Contains("\"missing\", which does not exist", error);
    }

    private (List<(string File, string PackagePath)>, List<string>) Resolve(string buildFile)
    {
        var engine = new RecordingBuildEngine();
        var task = new ResolveSmithySources
        {
            BuildEngine = engine,
            BuildFile = buildFile,
            ProjectDirectory = _directory,
        };
        task.Execute();
        var sources = task
            .Sources.Select(item =>
                (
                    Path.GetRelativePath(_directory, item.ItemSpec).Replace('\\', '/'),
                    item.GetMetadata("PackagePath")
                )
            )
            .ToList();
        return (sources, engine.Errors.Select(error => error.Message ?? "").ToList());
    }

    private string WriteFile(string relativePath, string content = "")
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
