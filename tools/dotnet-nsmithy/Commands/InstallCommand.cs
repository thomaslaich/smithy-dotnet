using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;

namespace DotnetNsmithy.Commands;

internal static class InstallCommand
{
    public static Command Create()
    {
        // No short aliases: -p would read like the MSBuild property switch passed after --.
        var projectOption = new Option<FileInfo?>("--project")
        {
            Description =
                "Project whose restored NSmithy package selects the CLI version. Without --project or --solution, the single solution, otherwise the single .csproj, in the current directory.",
        };
        var solutionOption = new Option<FileInfo?>("--solution")
        {
            Description =
                "Solution (.sln or .slnx) whose C# projects restore their CLI packages. Projects without NSmithy are skipped.",
        };
        var msbuildArguments = new Argument<string[]>("msbuild-arguments")
        {
            Description = "Arguments after -- are passed to dotnet msbuild, e.g. -- -p:Name=Value.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var command = new Command(
            "install",
            "Restore host-specific Smithy CLI packages for a project or solution."
        )
        {
            Options = { projectOption, solutionOption },
            Arguments = { msbuildArguments },
        };
        command.Validators.Add(result =>
        {
            var project = result.GetValue(projectOption);
            var solution = result.GetValue(solutionOption);
            if (project is not null && solution is not null)
                result.AddError("Specify either --project or --solution, not both.");
            else if (project is not null && InstallInput.IsSolution(project.FullName))
                result.AddError("--project expects a project file; use --solution for solutions.");
            else if (solution is not null && !InstallInput.IsSolution(solution.FullName))
                result.AddError("--solution expects a .sln or .slnx file.");
        });
        command.SetAction(
            (parseResult, cancellationToken) =>
                ExecuteAsync(
                    parseResult.GetValue(projectOption) ?? parseResult.GetValue(solutionOption),
                    parseResult.GetValue(msbuildArguments) ?? [],
                    cancellationToken
                )
        );
        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileInfo? input,
        string[] msbuildArguments,
        CancellationToken cancellationToken
    )
    {
        string? driver = null;
        try
        {
            var project = input?.FullName ?? InstallInput.Discover(Directory.GetCurrentDirectory());
            if (!File.Exists(project))
                throw new FileNotFoundException($"Project or solution does not exist: {project}");
            var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(project))!;
            if (InstallInput.IsSolution(project))
            {
                var projects = await InstallInput.ListSolutionProjectsAsync(
                    project,
                    cancellationToken
                );
                driver = Path.Combine(
                    Path.GetTempPath(),
                    $"nsmithy-install-{Guid.NewGuid():N}.proj"
                );
                InstallInput.CreateSolutionDriver(projects).Save(driver);
                project = driver;
            }
            // Delegate to each restored package: a global tool must not pick its own CLI version.
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            };
            start.ArgumentList.Add("msbuild");
            start.ArgumentList.Add(project);
            start.ArgumentList.Add(driver is null ? "-t:RestoreSmithyCli" : "-t:Install");
            start.ArgumentList.Add("-nologo");
            foreach (var argument in msbuildArguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            return process.ExitCode;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or InvalidOperationException
                        or UnauthorizedAccessException
                        or Win32Exception
            )
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            if (driver is not null)
                File.Delete(driver);
        }
    }
}
