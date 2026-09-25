using System.Text.Json;
using Microsoft.Build.Utilities;
using NSmithy.Contracts;

namespace NSmithy.Tests.MSBuild;

public sealed class SynthesizeSmithyBuildFileTests
{
    [Fact]
    public void SynthesizesFromLocalSourcesWithoutContractsBuildFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nsmithy-synthesis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "service.smithy");
            var output = Path.Combine(directory, "smithy-build.json");
            File.WriteAllText(source, "$version: \"2\"");

            var task = new SynthesizeSmithyBuildFile
            {
                Sources = [new TaskItem(source)],
                Service = "example#Service",
                CSharpCodegenVersion = "0.10.0",
                SmithyVersion = "1.73.0",
                OutputFile = output,
            };

            Assert.True(task.Execute());

            using var document = JsonDocument.Parse(File.ReadAllText(output));
            var root = document.RootElement;
            Assert.Equal(source, root.GetProperty("sources")[0].GetString());
            Assert.Equal(
                "example#Service",
                root.GetProperty("plugins")
                    .GetProperty("csharp-codegen")
                    .GetProperty("service")
                    .GetString()
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MergesExtensionDependenciesAndPlugins()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nsmithy-synthesis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var contractsBuild = Path.Combine(directory, "contracts-smithy-build.json");
            var source = Path.Combine(directory, "service.smithy");
            var output = Path.Combine(directory, "smithy-build.json");
            File.WriteAllText(
                contractsBuild,
                """
                {
                  "version": "1.0",
                  "maven": {
                    "dependencies": ["io.github.thomaslaich.bote:bote:0.1.0"]
                  }
                }
                """
            );
            File.WriteAllText(source, "$version: \"2\"");

            var asyncApi = new TaskItem("asyncapi");
            asyncApi.SetMetadata("Service", "example#Service");
            asyncApi.SetMetadata("SettingsJson", "{\"perspective\":\"client\"}");
            var task = new SynthesizeSmithyBuildFile
            {
                ContractsBuildFile = contractsBuild,
                Sources = [new TaskItem(source)],
                Service = "example#Service",
                CSharpCodegenVersion = "0.10.0",
                SmithyVersion = "1.73.0",
                AdditionalMavenDependencies =
                [
                    new TaskItem("io.github.thomaslaich.nsmithy:smithy-csharp-bote-codegen:0.10.0"),
                    new TaskItem("io.github.thomaslaich.bote:bote:0.1.0"),
                ],
                AdditionalPlugins = [asyncApi],
                OutputFile = output,
            };

            Assert.True(task.Execute());

            using var document = JsonDocument.Parse(File.ReadAllText(output));
            var root = document.RootElement;
            var dependencies = root.GetProperty("maven")
                .GetProperty("dependencies")
                .EnumerateArray()
                .Select(element => element.GetString()!)
                .ToArray();
            Assert.Equal(
                [
                    "io.github.thomaslaich.bote:bote:0.1.0",
                    "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.10.0",
                    "io.github.thomaslaich.nsmithy:smithy-csharp-bote-codegen:0.10.0",
                ],
                dependencies
            );

            var plugin = root.GetProperty("plugins").GetProperty("asyncapi");
            Assert.Equal("example#Service", plugin.GetProperty("service").GetString());
            Assert.Equal("client", plugin.GetProperty("perspective").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MergesMultipleContractsBuildFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nsmithy-synthesis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var applicationBuild = Path.Combine(directory, "application.json");
            var infrastructureBuild = Path.Combine(directory, "infrastructure.json");
            var source = Path.Combine(directory, "service.smithy");
            var output = Path.Combine(directory, "smithy-build.json");
            File.WriteAllText(
                applicationBuild,
                """
                {
                  "version": "1.0",
                  "maven": {
                    "dependencies": ["example:application-traits:1.0.0"]
                  }
                }
                """
            );
            File.WriteAllText(
                infrastructureBuild,
                """
                {
                  "version": "1.0",
                  "maven": {
                    "dependencies": ["example:infrastructure-traits:1.0.0"]
                  }
                }
                """
            );
            File.WriteAllText(source, "$version: \"2\"");

            var task = new SynthesizeSmithyBuildFile
            {
                ContractsBuildFiles =
                [
                    new TaskItem(applicationBuild),
                    new TaskItem(infrastructureBuild),
                ],
                Sources = [new TaskItem(source)],
                Service = "example#Service",
                CSharpCodegenVersion = "0.10.0",
                SmithyVersion = "1.73.0",
                OutputFile = output,
            };

            Assert.True(task.Execute());

            using var document = JsonDocument.Parse(File.ReadAllText(output));
            var dependencies = document
                .RootElement.GetProperty("maven")
                .GetProperty("dependencies")
                .EnumerateArray()
                .Select(element => element.GetString()!)
                .ToArray();
            Assert.Equal(
                [
                    "example:application-traits:1.0.0",
                    "example:infrastructure-traits:1.0.0",
                    "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.10.0",
                ],
                dependencies
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
