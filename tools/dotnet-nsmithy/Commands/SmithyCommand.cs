using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace DotnetNsmithy.Commands;

/// <summary>
/// Runs the Smithy CLI a project's build uses, with the same Maven setup. The project decides
/// which CLI that is (SmithyCliPath, the NSmithy.SmithyCli package, or smithy on PATH), so this
/// asks MSBuild instead of looking for one itself.
/// </summary>
internal static class SmithyCommand
{
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>("--project", "-p")
        {
            Description =
                "Project whose Smithy CLI to run. Auto-discovered in the current directory when omitted.",
        };
        var smithyArguments = new Argument<string[]>("smithy-arguments")
        {
            Description =
                "Arguments for the Smithy CLI. Put them after -- so options reach the CLI, e.g. -- validate.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var command = new Command(
            "smithy",
            "Run the Smithy CLI that a project's build uses, with the same Maven setup."
        )
        {
            Options = { projectOption },
            Arguments = { smithyArguments },
        };
        command.SetAction(
            (parseResult, cancellationToken) =>
                ExecuteAsync(
                    parseResult.GetValue(projectOption),
                    parseResult.GetValue(smithyArguments) ?? [],
                    cancellationToken
                )
        );
        return command;
    }

    internal static string DiscoverProject(string directory)
    {
        var projects = Directory.GetFiles(directory, "*.csproj");
        return projects.Length == 1
            ? projects[0]
            : throw new InvalidOperationException(
                "Specify --project <project.csproj>; the current directory must otherwise contain exactly one .csproj."
            );
    }

    internal static ResolvedCli ParseProperties(string json, string project)
    {
        // The JSON follows any restore output, which -verbosity:quiet keeps to errors.
        var start = json.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"MSBuild returned no properties for {project}.");
        using var document = JsonDocument.Parse(json[start..]);
        var properties = document.RootElement.GetProperty("Properties");
        string Get(string name) =>
            properties.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

        var path = Get("SmithyCliPath");
        if (path.Length == 0)
            throw new InvalidOperationException(
                $"{project} does not use NSmithy: it defines no SmithyCliPath."
            );

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            var pair in Get("NSmithySmithyCliEnvironment")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
                environment[pair[..separator]] = pair[(separator + 1)..];
        }
        return new ResolvedCli(path, Get("_SmithyCliFromPackage") == "true", environment);
    }

    private static async Task<int> ExecuteAsync(
        FileInfo? projectFile,
        string[] smithyArguments,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var project = projectFile?.FullName ?? DiscoverProject(Directory.GetCurrentDirectory());
            if (!File.Exists(project))
                throw new FileNotFoundException($"Project does not exist: {project}");

            var cli = await ResolveAsync(project, cancellationToken);
            using var process = Process.Start(CreateStartInfo(cli, smithyArguments))!;
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
                        or InvalidOperationException
                        or JsonException
                        or Win32Exception
            )
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    // -restore first, so a referenced NSmithy.SmithyCli package is in place before the project
    // resolves the CLI's path from it.
    private static async Task<ResolvedCli> ResolveAsync(
        string project,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(project))!,
        };
        foreach (
            var argument in new[]
            {
                "msbuild",
                project,
                "-restore",
                "-nologo",
                "-verbosity:quiet",
                "-getProperty:SmithyCliPath",
                "-getProperty:_SmithyCliFromPackage",
                "-getProperty:NSmithySmithyCliEnvironment",
            }
        )
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Could not evaluate {project}:{Environment.NewLine}{(await output).Trim()}{(await error).Trim()}"
            );
        return ParseProperties(await output, project);
    }

    private static ProcessStartInfo CreateStartInfo(ResolvedCli cli, string[] arguments)
    {
        // NuGet does not keep the executable bit; the build sets it the same way.
        if (cli.FromPackage && !OperatingSystem.IsWindows() && File.Exists(cli.Path))
            File.SetUnixFileMode(
                cli.Path,
                File.GetUnixFileMode(cli.Path)
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute
            );

        // The Windows launcher is a batch file, which only cmd.exe runs.
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", cli.Path } }
            : new ProcessStartInfo(cli.Path);
        start.UseShellExecute = false;
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var (name, value) in cli.Environment)
            start.Environment[name] = value;
        return start;
    }

    internal sealed record ResolvedCli(
        string Path,
        bool FromPackage,
        IReadOnlyDictionary<string, string> Environment
    );
}
