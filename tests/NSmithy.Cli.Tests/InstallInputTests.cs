using DotnetNsmithy.Commands;

namespace NSmithy.Cli.Tests;

public sealed class InstallInputTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"nsmithy solution {Guid.NewGuid():N}"
    );

    public InstallInputTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void SolutionDiscoveryTakesPrecedenceOverProject()
    {
        File.WriteAllText(Path.Combine(directory, "App.csproj"), "");
        var solution = Path.Combine(directory, "App.slnx");
        File.WriteAllText(solution, "");
        Assert.Equal(solution, InstallInput.Discover(directory));
        File.WriteAllText(Path.Combine(directory, "Other.slnx"), "");
        Assert.Throws<InvalidOperationException>(() => InstallInput.Discover(directory));
    }

    [Fact]
    public void DriverResolvesNestedProjectsRelativeToSolutionAndSkipsAbsentTargets()
    {
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        File.WriteAllText(Path.Combine(directory, "nested", "App.csproj"), "");
        var solution = Path.Combine(directory, "App.slnx");
        File.WriteAllText(
            solution,
            """
            <Solution>
              <Folder Name="/Applications/">
                <Project Path="nested\App.csproj" />
                <Project Path="nested/App.csproj" />
                <Project Path="native.vcxproj" />
              </Folder>
            </Solution>
            """
        );
        var driver = InstallInput.CreateSolutionDriver(solution);
        var task = Assert.Single(driver.Descendants("MSBuild"));
        Assert.Equal(
            Path.Combine(directory, "nested", "App.csproj"),
            (string?)task.Attribute("Projects")
        );
        Assert.Equal("true", (string?)task.Attribute("SkipNonexistentTargets"));
    }

    [Theory]
    [InlineData("<Project />")]
    [InlineData("<Solution />")]
    public void RejectsInvalidOrEmptySolution(string contents)
    {
        var solution = Path.Combine(directory, "App.slnx");
        File.WriteAllText(solution, contents);
        Assert.Throws<InvalidDataException>(() => InstallInput.CreateSolutionDriver(solution));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
