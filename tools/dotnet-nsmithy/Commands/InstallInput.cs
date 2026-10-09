using System.Xml.Linq;

namespace DotnetNsmithy.Commands;

internal static class InstallInput
{
    internal static string Discover(string directory)
    {
        var solutions = Directory.GetFiles(directory, "*.slnx");
        if (solutions.Length == 1)
            return solutions[0];
        var projects = Directory.GetFiles(directory, "*.csproj");
        if (solutions.Length == 0 && projects.Length == 1)
            return projects[0];
        throw new InvalidOperationException(
            "Specify --solution <solution.slnx> or --project <project.csproj>. Otherwise the current directory must contain one .slnx, or one .csproj and no .slnx. Run dotnet restore first."
        );
    }

    internal static XDocument CreateSolutionDriver(string solution)
    {
        var document = XDocument.Load(solution);
        if (document.Root?.Name != "Solution")
            throw new InvalidDataException("Expected a .slnx file with a Solution root element.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(solution))!;
        var projects = document
            .Root.Descendants("Project")
            .Select(project => (string?)project.Attribute("Path"))
            .Where(path => path?.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) == true)
            .Select(path =>
                Path.GetFullPath(path!.Replace('\\', Path.DirectorySeparatorChar), directory)
            )
            .Distinct(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            )
            .ToArray();
        if (projects.Length == 0)
            throw new InvalidDataException("The solution contains no C# projects.");
        foreach (var project in projects)
            if (!File.Exists(project))
                throw new FileNotFoundException(
                    $"Solution project does not exist: {project}",
                    project
                );

        // A solution can mix NSmithy and ordinary projects. Let MSBuild evaluate each project
        // and skip absent install targets; do not guess from literal PackageReference entries.
        return new XDocument(
            new XElement(
                "Project",
                new XElement(
                    "Target",
                    new XAttribute("Name", "Install"),
                    projects.Select(project => new XElement(
                        "MSBuild",
                        new XAttribute("Projects", Escape(project)),
                        new XAttribute("Targets", "InstallSmithyCli"),
                        new XAttribute("SkipNonexistentTargets", "true"),
                        new XAttribute("BuildInParallel", "false")
                    ))
                )
            )
        );
    }

    private static string Escape(string value) =>
        string.Concat(
            value.Select(character =>
                character switch
                {
                    '%' => "%25",
                    '$' => "%24",
                    '@' => "%40",
                    ';' => "%3B",
                    '\'' => "%27",
                    '(' => "%28",
                    ')' => "%29",
                    '*' => "%2A",
                    '?' => "%3F",
                    _ => character.ToString(),
                }
            )
        );
}
