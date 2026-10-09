using System.Diagnostics;
using System.Xml.Linq;

namespace DotnetNsmithy.Commands;

internal static class InstallInput
{
    internal static bool IsSolution(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx";

    internal static string Discover(string directory)
    {
        // "*.sln" also matches ".slnx" on Windows, so filter by the exact extension.
        var solutions = Directory.GetFiles(directory, "*.sln*").Where(IsSolution).ToArray();
        if (solutions.Length == 1)
            return solutions[0];
        var projects = Directory.GetFiles(directory, "*.csproj");
        if (solutions.Length == 0 && projects.Length == 1)
            return projects[0];
        throw new InvalidOperationException(
            "Specify --solution <solution> or --project <project.csproj>. Otherwise the current directory must contain one .sln or .slnx, or one .csproj and no solution."
        );
    }

    // `dotnet sln list` reads both .sln and .slnx. Its header is localized, so keep only the
    // lines naming a C# project; paths are relative to the solution.
    internal static async Task<string[]> ListSolutionProjectsAsync(
        string solution,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("sln");
        start.ArgumentList.Add(solution);
        start.ArgumentList.Add("list");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidDataException(
                $"Could not list the projects of {solution}: {(await error).Trim()}"
            );
        return ParseSolutionProjects(await output, Path.GetDirectoryName(solution)!);
    }

    internal static string[] ParseSolutionProjects(string listOutput, string solutionDirectory)
    {
        var projects = listOutput
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(path =>
                Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar), solutionDirectory)
            )
            .Distinct(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            )
            .ToArray();
        if (projects.Length == 0)
            throw new InvalidDataException("The solution contains no C# projects.");
        return projects;
    }

    internal static XDocument CreateSolutionDriver(IEnumerable<string> projects)
    {
        var paths = projects.ToArray();
        foreach (var project in paths)
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
                    paths.Select(project => new XElement(
                        "MSBuild",
                        new XAttribute("Projects", Escape(project)),
                        new XAttribute("Targets", "RestoreSmithyCli"),
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
