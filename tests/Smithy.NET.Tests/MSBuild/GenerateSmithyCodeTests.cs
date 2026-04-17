using Microsoft.Build.Utilities;
using Smithy.NET.MSBuild;

namespace Smithy.NET.Tests.MSBuild;

public sealed class GenerateSmithyCodeTests
{
    [Fact]
    public void ExecuteRunsSmithyBuildAndWritesGeneratedCompileItems()
    {
        using var directory = TemporaryDirectory.Create();
        var modelDirectory = Path.Combine(directory.Path, "model");
        var buildOutputDirectory = Path.Combine(directory.Path, "smithy-build");
        var generatedOutputDirectory = Path.Combine(directory.Path, "generated");
        Directory.CreateDirectory(modelDirectory);

        File.WriteAllText(
            Path.Combine(directory.Path, "smithy-build.json"),
            """
            {
              "version": "1.0",
              "sources": ["model"],
              "projections": {
                "source": {}
              }
            }
            """
        );
        File.WriteAllText(
            Path.Combine(modelDirectory, "weather.smithy"),
            """
            $version: "2"

            namespace example.weather

            structure Forecast {
                @required
                city: String
            }
            """
        );

        var task = new GenerateSmithyCode
        {
            WorkingDirectory = directory.Path,
            BuildFile = "smithy-build.json",
            OutputDirectory = buildOutputDirectory,
            GeneratedOutputDirectory = generatedOutputDirectory,
        };

        Assert.True(task.Execute());

        var generatedFile = Assert.Single(task.GeneratedFiles);
        Assert.Equal(
            Path.Combine(generatedOutputDirectory, "Example", "Weather", "Forecast.g.cs"),
            generatedFile.ItemSpec
        );
        Assert.Contains(
            "public sealed partial record class Forecast",
            File.ReadAllText(generatedFile.ItemSpec)
        );
    }

    [Fact]
    public void ExecuteCanGenerateSyntheticSmithyBuildFileFromSmithyModelItems()
    {
        using var directory = TemporaryDirectory.Create();
        var buildOutputDirectory = Path.Combine(directory.Path, "smithy-build");
        var generatedOutputDirectory = Path.Combine(directory.Path, "generated");
        var modelFile = Path.Combine(directory.Path, "weather.smithy");

        File.WriteAllText(
            modelFile,
            """
            $version: "2"

            namespace example.weather

            structure Forecast {
                @required
                city: String
            }
            """
        );

        var task = new GenerateSmithyCode
        {
            WorkingDirectory = directory.Path,
            BuildFile = "smithy-build.json",
            OutputDirectory = buildOutputDirectory,
            GeneratedOutputDirectory = generatedOutputDirectory,
            SmithyModel = [new TaskItem(modelFile)],
        };

        Assert.True(task.Execute());

        var generatedFile = Assert.Single(task.GeneratedFiles);
        Assert.Equal(
            Path.Combine(generatedOutputDirectory, "Example", "Weather", "Forecast.g.cs"),
            generatedFile.ItemSpec
        );
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "smithy-net-tests",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
