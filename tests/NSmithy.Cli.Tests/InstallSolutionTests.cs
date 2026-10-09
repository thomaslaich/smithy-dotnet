using System.Xml.Linq;
using DotnetNsmithy.Commands;

namespace NSmithy.Cli.Tests;

public sealed class InstallSolutionTests
{
    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public async Task SolutionListingHonorsSolutionsGlobalJson(string format)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nsmithy sdk {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            // An unavailable SDK makes selection observable without requiring multiple installed SDKs.
            File.WriteAllText(
                Path.Combine(directory, "global.json"),
                """{"sdk":{"version":"99.0.100","rollForward":"disable"}}"""
            );
            var solution = Path.Combine(directory, "Applications" + format);
            if (format == ".slnx")
                WriteSlnx(solution);
            else
                WriteSln(solution);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                InstallInput.ListSolutionProjectsAsync(solution, CancellationToken.None)
            );
            Assert.Contains("99.0.100", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(".slnx", false)]
    [InlineData(".slnx", true)]
    [InlineData(".sln", false)]
    public async Task InstallsMixedSolutionAndPropagatesProjectFailures(string format, bool fail)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nsmithy solution {Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        try
        {
            var first = Path.Combine(directory, "nested", "First; App.csproj");
            var second = Path.Combine(directory, "nested", "Second.csproj");
            var ordinary = Path.Combine(directory, "Ordinary.csproj");
            var firstMarker = Path.Combine(directory, "first-installed");
            var secondMarker = Path.Combine(directory, "second-installed");
            WriteProject(first, firstMarker, "first-pin", fail);
            WriteProject(second, secondMarker, "second-pin", fail: false);
            File.WriteAllText(ordinary, "<Project />");
            var solution = Path.Combine(directory, "Applications" + format);
            if (format == ".slnx")
                WriteSlnx(solution);
            else
                WriteSln(solution);

            // The property after -- reaches every project through the solution driver.
            var exitCode = await InstallCommand
                .Create()
                .Parse(["--solution", solution, "--", "-p:PinSuffix=-forwarded"])
                .InvokeAsync();

            if (fail)
            {
                Assert.NotEqual(0, exitCode);
                Assert.False(File.Exists(secondMarker));
            }
            else
            {
                Assert.Equal(0, exitCode);
                Assert.Equal(["first-pin-forwarded"], File.ReadAllLines(firstMarker));
                Assert.Equal(["second-pin-forwarded"], File.ReadAllLines(secondMarker));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("--project", "App.slnx")]
    [InlineData("--solution", "App.csproj")]
    public async Task RejectsMismatchedInputKind(string option, string file)
    {
        var exitCode = await InstallCommand.Create().Parse([option, file]).InvokeAsync();
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task RejectsProjectAndSolutionTogether()
    {
        var exitCode = await InstallCommand
            .Create()
            .Parse(["--project", "App.csproj", "--solution", "App.slnx"])
            .InvokeAsync();
        Assert.NotEqual(0, exitCode);
    }

    private static void WriteSlnx(string path) =>
        new XDocument(
            new XElement(
                "Solution",
                new XElement("Project", new XAttribute("Path", "Ordinary.csproj")),
                new XElement(
                    "Folder",
                    new XAttribute("Name", "/Apps/"),
                    new XElement("Project", new XAttribute("Path", "nested/First; App.csproj")),
                    new XElement("Project", new XAttribute("Path", "nested/Second.csproj"))
                )
            )
        ).Save(path);

    private static void WriteSln(string path) =>
        File.WriteAllText(
            path,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Ordinary", "Ordinary.csproj", "{6A1D7C2E-0F0B-4C1E-9D7A-1B2C3D4E5F01}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "First; App", "nested\First; App.csproj", "{6A1D7C2E-0F0B-4C1E-9D7A-1B2C3D4E5F02}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Second", "nested\Second.csproj", "{6A1D7C2E-0F0B-4C1E-9D7A-1B2C3D4E5F03}"
            EndProject
            Global
            EndGlobal
            """
        );

    private static void WriteProject(string path, string marker, string pin, bool fail)
    {
        new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "Target",
                    new XAttribute("Name", "RestoreSmithyCli"),
                    fail
                        ? new XElement("Error", new XAttribute("Text", "Expected install failure"))
                        : new XElement(
                            "WriteLinesToFile",
                            new XAttribute("File", marker),
                            new XAttribute("Lines", pin + "$(PinSuffix)")
                        )
                )
            )
        ).Save(path);
    }
}
