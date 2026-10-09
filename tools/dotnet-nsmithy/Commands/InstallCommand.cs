using System.CommandLine;
using System.Diagnostics;

namespace DotnetNsmithy.Commands;

internal static class InstallCommand
{
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project", "-p")
        {
            Description =
                "Project whose restored NSmithy package selects the CLI version. Defaults to the single .csproj in the current directory.",
        };
        var command = new Command(
            "install",
            "Install the project's pinned Smithy CLI and Java runtime for this host."
        )
        {
            Options = { projectOption },
        };
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var project = parseResult.GetValue(projectOption)?.FullName;
                if (project is null)
                {
                    var projects = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
                    if (projects.Length != 1)
                    {
                        Console.Error.WriteLine(
                            "Specify --project <project.csproj>, or run in a directory with exactly one .csproj. Run dotnet restore first."
                        );
                        return 1;
                    }
                    project = projects[0];
                }
                if (!File.Exists(project))
                {
                    Console.Error.WriteLine($"Project does not exist: {project}");
                    return 1;
                }
                // Delegate to the restored package: a global tool must not pick its own CLI version.
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
                start.ArgumentList.Add("msbuild");
                start.ArgumentList.Add(project);
                start.ArgumentList.Add("-t:InstallSmithyCli");
                start.ArgumentList.Add("-nologo");
                using var process = Process.Start(start)!;
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw;
                }
                return process.ExitCode;
            }
        );
        return command;
    }
}
