using System.Xml.Linq;
using DotnetNsmithy.Commands;

namespace NSmithy.Cli.Tests;

public sealed class InstallSolutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallsMixedSolutionAndPropagatesProjectFailures(bool fail)
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
            var solution = Path.Combine(directory, "Applications.slnx");
            new XDocument(
                new XElement(
                    "Solution",
                    new XElement("Project", new XAttribute("Path", "Ordinary.csproj")),
                    new XElement(
                        "Folder",
                        new XAttribute("Name", "/Apps/"),
                        new XElement("Project", new XAttribute("Path", "nested/First; App.csproj")),
                        new XElement("Project", new XAttribute("Path", "nested/First; App.csproj")),
                        new XElement("Project", new XAttribute("Path", "nested/Second.csproj"))
                    )
                )
            ).Save(solution);

            var exitCode = await InstallCommand
                .Create()
                .Parse(["--solution", solution])
                .InvokeAsync();

            if (fail)
            {
                Assert.NotEqual(0, exitCode);
                Assert.False(File.Exists(secondMarker));
            }
            else
            {
                Assert.Equal(0, exitCode);
                Assert.Equal(["first-pin"], File.ReadAllLines(firstMarker));
                Assert.Equal(["second-pin"], File.ReadAllLines(secondMarker));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteProject(string path, string marker, string pin, bool fail)
    {
        new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "Target",
                    new XAttribute("Name", "InstallSmithyCli"),
                    fail
                        ? new XElement("Error", new XAttribute("Text", "Expected install failure"))
                        : new XElement(
                            "WriteLinesToFile",
                            new XAttribute("File", marker),
                            new XAttribute("Lines", pin)
                        )
                )
            )
        ).Save(path);
    }
}
