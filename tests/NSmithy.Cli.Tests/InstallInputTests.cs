using DotnetNsmithy.Commands;

namespace NSmithy.Cli.Tests;

public sealed class InstallInputTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"nsmithy solution {Guid.NewGuid():N}"
    );

    public InstallInputTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    public void SolutionDiscoveryTakesPrecedenceOverProject(string solutionName)
    {
        File.WriteAllText(Path.Combine(directory, "App.csproj"), "");
        var solution = Path.Combine(directory, solutionName);
        File.WriteAllText(solution, "");
        Assert.Equal(solution, InstallInput.Discover(directory));
        File.WriteAllText(Path.Combine(directory, "Other.slnx"), "");
        Assert.Throws<InvalidOperationException>(() => InstallInput.Discover(directory));
    }

    [Fact]
    public void ParsesSolutionListIntoDistinctCSharpProjects()
    {
        // `dotnet sln list` output: a localized header, then paths relative to the solution.
        const string output = """
            Project(s)
            ----------
            nested\App.csproj
            nested/App.csproj
            native.vcxproj
            Other.fsproj

            """;
        var projects = InstallInput.ParseSolutionProjects(output, directory);
        Assert.Equal([Path.Combine(directory, "nested", "App.csproj")], projects);
    }

    [Fact]
    public void RejectsSolutionWithoutCSharpProjects() =>
        Assert.Throws<InvalidDataException>(() =>
            InstallInput.ParseSolutionProjects("Project(s)\n----------\nOther.fsproj\n", directory)
        );

    [Fact]
    public void DriverSkipsAbsentTargetsAndEscapesPaths()
    {
        var project = Path.Combine(directory, "First; App.csproj");
        File.WriteAllText(project, "");
        var driver = InstallInput.CreateSolutionDriver([project]);
        var task = Assert.Single(driver.Descendants("MSBuild"));
        Assert.Equal(
            Path.Combine(directory, "First%3B App.csproj"),
            (string?)task.Attribute("Projects")
        );
        Assert.Equal("true", (string?)task.Attribute("SkipNonexistentTargets"));
    }

    [Fact]
    public void DriverRejectsMissingProject() =>
        Assert.Throws<FileNotFoundException>(() =>
            InstallInput.CreateSolutionDriver([Path.Combine(directory, "Missing.csproj")])
        );

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
