using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.Xml;

namespace DotnetNsmithy.Commands;

internal static class InstallCommand
{
    public static Command Create()
    {
        var inputOption = new Option<FileInfo?>("--project", "-p", "--solution", "-s")
        {
            Description =
                "Project or .slnx solution whose restored NSmithy packages select the CLI versions. Defaults to the single .slnx, otherwise the single .csproj in the current directory.",
        };
        var command = new Command(
            "install",
            "Install pinned Smithy CLIs and Java runtimes for a project or .slnx solution."
        )
        {
            Options = { inputOption },
        };
        command.SetAction(
            (parseResult, cancellationToken) =>
                ExecuteAsync(parseResult.GetValue(inputOption), cancellationToken)
        );
        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileInfo? input,
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
            if (Path.GetExtension(project).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                var document = InstallInput.CreateSolutionDriver(project);
                driver = Path.Combine(
                    Path.GetTempPath(),
                    $"nsmithy-install-{Guid.NewGuid():N}.proj"
                );
                document.Save(driver);
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
            start.ArgumentList.Add(driver is null ? "-t:InstallSmithyCli" : "-t:Install");
            start.ArgumentList.Add("-nologo");
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
                        or XmlException
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
