using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NSmithy.Contracts;

namespace NSmithy.Tests.MSBuild;

public sealed class CreateSmithyJarTests : IDisposable
{
    private static readonly XNamespace Pom = "http://maven.apache.org/POM/4.0.0";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nsmithy-jar-{Guid.NewGuid():N}"
    );

    public CreateSmithyJarTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void PomDeclaresMavenDependenciesOfTheBuildFile()
    {
        // A consumer resolves the model JAR alone, so the traits the model uses must come with it.
        var buildFile = WriteFile(
            "smithy-build.json",
            """
            {
              // Comments are allowed, as in every smithy-build.json NSmithy reads.
              "version": "1.0",
              "sources": ["model"],
              "maven": {
                "dependencies": [
                  "software.amazon.smithy:smithy-aws-traits:1.74.1",
                  "com.disneystreaming.alloy:alloy-core:0.3.40",
                ],
              },
            }
            """
        );

        var (pom, errors) = Create(buildFile);

        Assert.Empty(errors);
        Assert.Equal(
            [
                ("software.amazon.smithy", "smithy-aws-traits", "1.74.1"),
                ("com.disneystreaming.alloy", "alloy-core", "0.3.40"),
            ],
            pom.Root!.Element(Pom + "dependencies")!
                .Elements(Pom + "dependency")
                .Select(dependency =>
                    (
                        dependency.Element(Pom + "groupId")!.Value,
                        dependency.Element(Pom + "artifactId")!.Value,
                        dependency.Element(Pom + "version")!.Value
                    )
                )
        );
    }

    [Fact]
    public void PomWithoutBuildFileDeclaresNoDependencies()
    {
        var (pom, errors) = Create(buildFile: null);

        Assert.Empty(errors);
        Assert.Null(pom.Root!.Element(Pom + "dependencies"));
        Assert.Equal("1.0.0", pom.Root.Element(Pom + "version")!.Value);
    }

    [Fact]
    public void PomElementsAreInThePomNamespace()
    {
        var (pom, _) = Create(buildFile: null);

        // An element outside the namespace renders as xmlns="", which strict POM readers reject.
        Assert.All(
            pom.Root!.DescendantsAndSelf(),
            element => Assert.Equal(Pom, element.Name.Namespace)
        );
    }

    [Fact]
    public void InvalidDependencyCoordinateFails()
    {
        var buildFile = WriteFile(
            "smithy-build.json",
            """{ "version": "1.0", "maven": { "dependencies": ["smithy-aws-traits"] } }"""
        );

        var (_, errors) = Create(buildFile);

        var error = Assert.Single(errors);
        Assert.Contains("smithy-aws-traits", error.Message, StringComparison.Ordinal);
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private (XDocument Pom, List<BuildErrorEventArgs> Errors) Create(string? buildFile)
    {
        var source = WriteFile("model/main.smithy", "$version: \"2\"\nnamespace example\n");
        var output = Path.Combine(_directory, "out");
        var engine = new RecordingBuildEngine();
        var task = new CreateSmithyJar
        {
            BuildEngine = engine,
            Sources = [new TaskItem(source)],
            SourcesRoot = Path.Combine(_directory, "model"),
            GroupId = "io.github.acme",
            ArtifactId = "contracts",
            Version = "1.0.0",
            OutputDirectory = output,
            BuildFile = buildFile ?? "",
        };

        var succeeded = task.Execute();

        Assert.Equal(engine.Errors.Count == 0, succeeded);
        var pomPath = Path.Combine(output, "contracts-1.0.0.pom");
        var pom = succeeded ? XDocument.Load(pomPath) : new XDocument();
        return (pom, engine.Errors);
    }
}
