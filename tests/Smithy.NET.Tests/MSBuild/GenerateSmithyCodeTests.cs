using System.Diagnostics;
using System.Text.Json;
using Microsoft.Build.Utilities;
using Smithy.NET.CodeGeneration;
using Smithy.NET.Core;
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

    [Fact]
    public void ExecuteCanLimitGeneratedFilesToConfiguredNamespaces()
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

            structure Forecast {}
            """
        );
        File.WriteAllText(
            Path.Combine(modelDirectory, "aws.smithy"),
            """
            $version: "2"

            namespace aws.api

            structure Service {}
            """
        );

        var task = new GenerateSmithyCode
        {
            WorkingDirectory = directory.Path,
            BuildFile = "smithy-build.json",
            OutputDirectory = buildOutputDirectory,
            GeneratedOutputDirectory = generatedOutputDirectory,
            GeneratedNamespaces = "example.weather",
        };

        Assert.True(task.Execute());

        var generatedFile = Assert.Single(task.GeneratedFiles);
        Assert.Equal(
            Path.Combine(generatedOutputDirectory, "Example", "Weather", "Forecast.g.cs"),
            generatedFile.ItemSpec
        );
    }

    [Fact]
    public void ExecuteDeletesFilesFromPreviousGeneratedFileManifest()
    {
        using var directory = TemporaryDirectory.Create();
        var buildOutputDirectory = Path.Combine(directory.Path, "smithy-build");
        var generatedOutputDirectory = Path.Combine(directory.Path, "generated");
        var generatedFileManifest = Path.Combine(buildOutputDirectory, "generated-files.json");
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

        var firstTask = new GenerateSmithyCode
        {
            WorkingDirectory = directory.Path,
            BuildFile = "smithy-build.json",
            OutputDirectory = buildOutputDirectory,
            GeneratedOutputDirectory = generatedOutputDirectory,
            GeneratedFileManifest = generatedFileManifest,
            SmithyModel = [new TaskItem(modelFile)],
        };

        Assert.True(firstTask.Execute());
        var staleFile = Assert.Single(firstTask.GeneratedFiles).ItemSpec;
        Assert.True(File.Exists(staleFile));

        File.WriteAllText(
            modelFile,
            """
            $version: "2"

            namespace example.weather

            structure Observation {
                @required
                city: String
            }
            """
        );

        var secondTask = new GenerateSmithyCode
        {
            WorkingDirectory = directory.Path,
            BuildFile = "smithy-build.json",
            OutputDirectory = buildOutputDirectory,
            GeneratedOutputDirectory = generatedOutputDirectory,
            GeneratedFileManifest = generatedFileManifest,
            SmithyModel = [new TaskItem(modelFile)],
        };

        Assert.True(secondTask.Execute());

        var generatedFile = Assert.Single(secondTask.GeneratedFiles).ItemSpec;
        Assert.False(File.Exists(staleFile));
        Assert.Equal(
            Path.Combine(generatedOutputDirectory, "Example", "Weather", "Observation.g.cs"),
            generatedFile
        );
        Assert.Contains(generatedFile, File.ReadAllText(generatedFileManifest));
    }

    [Fact]
    public void ExecuteWritesDependencyManifest()
    {
        using var directory = TemporaryDirectory.Create();
        var buildOutputDirectory = Path.Combine(directory.Path, "smithy-build");
        var generatedOutputDirectory = Path.Combine(directory.Path, "generated");
        var dependencyManifest = Path.Combine(buildOutputDirectory, "dependencies.json");
        var dependencyInputFile = Path.Combine(buildOutputDirectory, "dependency-inputs.txt");
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
            DependencyManifest = dependencyManifest,
            DependencyInputFile = dependencyInputFile,
            SmithyModel = [new TaskItem("weather.smithy")],
        };

        Assert.True(task.Execute());

        using var document = JsonDocument.Parse(File.ReadAllText(dependencyManifest));
        var root = document.RootElement;
        Assert.Equal(
            Path.GetFullPath(modelFile),
            root.GetProperty("ConfiguredModelInputs")[0].GetString()
        );
        Assert.Contains(
            Path.GetFullPath(modelFile),
            root.GetProperty("DependencyInputs")
                .EnumerateArray()
                .Select(element => element.GetString())
        );
        Assert.EndsWith(
            Path.Combine("source", "model", "model.json"),
            root.GetProperty("ModelPath").GetString(),
            StringComparison.Ordinal
        );
        Assert.NotEmpty(root.GetProperty("SmithySourceArtifacts").EnumerateArray());
        Assert.Contains(Path.GetFullPath(modelFile), File.ReadAllLines(dependencyInputFile));
    }

    [Fact]
    public void ExecuteTracksBuildFileSourcesAndImportsInDependencyInputs()
    {
        using var directory = TemporaryDirectory.Create();
        var modelDirectory = Path.Combine(directory.Path, "model");
        var importsDirectory = Path.Combine(directory.Path, "imports");
        var buildOutputDirectory = Path.Combine(directory.Path, "smithy-build");
        var generatedOutputDirectory = Path.Combine(directory.Path, "generated");
        var dependencyInputFile = Path.Combine(buildOutputDirectory, "dependency-inputs.txt");
        var modelFile = Path.Combine(modelDirectory, "weather.smithy");
        var importFile = Path.Combine(importsDirectory, "common.smithy");
        Directory.CreateDirectory(modelDirectory);
        Directory.CreateDirectory(importsDirectory);

        File.WriteAllText(
            Path.Combine(directory.Path, "smithy-build.json"),
            """
            {
              "version": "1.0",
              "sources": ["model"],
              "imports": ["imports/common.smithy"],
              "projections": {
                "source": {}
              }
            }
            """
        );
        File.WriteAllText(
            modelFile,
            """
            $version: "2"

            namespace example.weather

            use example.common#Metadata

            structure Forecast {
                metadata: Metadata
            }
            """
        );
        File.WriteAllText(
            importFile,
            """
            $version: "2"

            namespace example.common

            structure Metadata {
                source: String
            }
            """
        );

        var task = new GenerateSmithyCode
        {
            WorkingDirectory = directory.Path,
            BuildFile = "smithy-build.json",
            OutputDirectory = buildOutputDirectory,
            GeneratedOutputDirectory = generatedOutputDirectory,
            DependencyInputFile = dependencyInputFile,
        };

        Assert.True(task.Execute());

        var dependencyInputs = File.ReadAllLines(dependencyInputFile);
        Assert.Contains(Path.GetFullPath(modelFile), dependencyInputs);
        Assert.Contains(Path.GetFullPath(importFile), dependencyInputs);
    }

    [Fact]
    public async System.Threading.Tasks.Task TargetsGenerateCompileItemsForConsumerProject()
    {
        using var directory = TemporaryDirectory.Create();
        var packageDirectory = Path.Combine(directory.Path, "package");
        var projectDirectory = Path.Combine(directory.Path, "consumer");
        var modelDirectory = Path.Combine(projectDirectory, "model");
        Directory.CreateDirectory(Path.Combine(packageDirectory, "build"));
        Directory.CreateDirectory(Path.Combine(packageDirectory, "tasks"));
        Directory.CreateDirectory(modelDirectory);

        File.Copy(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "Smithy.NET.MSBuild",
                "Targets",
                "Smithy.NET.MSBuild.targets"
            ),
            Path.Combine(packageDirectory, "build", "Smithy.NET.MSBuild.targets")
        );
        CopyAssemblyToTasks(typeof(GenerateSmithyCode).Assembly.Location, packageDirectory);
        CopyAssemblyToTasks(typeof(SmithyBuildRunner).Assembly.Location, packageDirectory);
        CopyAssemblyToTasks(typeof(ShapeId).Assembly.Location, packageDirectory);

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
        File.WriteAllText(
            Path.Combine(projectDirectory, "ConsumerProject.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <SmithyGeneratedOutputPath>obj/Smithy/</SmithyGeneratedOutputPath>
                <SmithyBuildOutputPath>obj/SmithyBuild/</SmithyBuildOutputPath>
              </PropertyGroup>

              <ItemGroup>
                <SmithyModel Include="model/**/*.smithy" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/Smithy.NET.Core/Smithy.NET.Core.csproj" />
              </ItemGroup>

              <Import Project="{{Path.Combine(
                packageDirectory,
                "build",
                "Smithy.NET.MSBuild.targets"
            )}}" />
            </Project>
            """
        );
        File.WriteAllText(
            Path.Combine(projectDirectory, "Consumer.cs"),
            """
            using ConsumerProject.Example.Weather;

            namespace ConsumerProject;

            public static class Consumer
            {
                public static Forecast Create()
                {
                    return new Forecast("Zurich");
                }
            }
            """
        );

        var result = await RunDotNetBuild(projectDirectory);

        Assert.True(
            result.ExitCode == 0,
            $"dotnet build failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}{Environment.NewLine}{result.Error}"
        );
        Assert.True(
            File.Exists(
                Path.Combine(
                    projectDirectory,
                    "obj",
                    "Smithy",
                    "Example",
                    "Weather",
                    "Forecast.g.cs"
                )
            )
        );

        var secondResult = await RunDotNetBuild(projectDirectory);

        Assert.True(
            secondResult.ExitCode == 0,
            $"second dotnet build failed with exit code {secondResult.ExitCode}.{Environment.NewLine}{secondResult.Output}{Environment.NewLine}{secondResult.Error}"
        );
    }

    private static void CopyAssemblyToTasks(string assemblyPath, string packageDirectory)
    {
        File.Copy(
            assemblyPath,
            Path.Combine(packageDirectory, "tasks", Path.GetFileName(assemblyPath)),
            overwrite: true
        );
    }

    private static async System.Threading.Tasks.Task<(
        int ExitCode,
        string Output,
        string Error
    )> RunDotNetBuild(string projectDirectory)
    {
        using var process = Process.Start(
            new ProcessStartInfo("dotnet", ["build", "-p:UseSharedCompilation=false"])
            {
                WorkingDirectory = projectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        );

        Assert.NotNull(process);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Smithy.NET.slnx"))
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not find the Smithy.NET repository root."
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
