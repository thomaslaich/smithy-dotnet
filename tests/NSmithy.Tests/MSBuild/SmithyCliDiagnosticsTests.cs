using System.Diagnostics;
using System.Text;

namespace NSmithy.Tests.MSBuild;

public sealed class SmithyCliDiagnosticsTests
{
    [Fact]
    public async Task GenerateSmithyCodeSurfacesSmithyCliDiagnostics()
    {
        var repoRoot = FindRepoRoot();
        var projectPath = Path.Combine(
            repoRoot,
            "tests",
            "Conformance",
            "SimpleRestJson",
            "SimpleRestJson.Conformance.csproj"
        );
        var stampFile = Path.Combine(
            Path.GetTempPath(),
            $"nsmithy-cli-diagnostics-{Guid.NewGuid():N}.stamp"
        );

        var result = await RunDotnetBuildAsync(
            repoRoot,
            [
                "build",
                projectPath,
                "--configuration",
                "Release",
                "--no-restore",
                "/t:GenerateSmithyCode",
                $"/p:SmithyStampFile={stampFile}",
                "/p:SmithyExtraArgs=--definitely-not-a-smithy-flag",
            ]
        );

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unexpected CLI argument: --definitely-not-a-smithy-flag", result.Output);
        Assert.Contains("error NSMITHYCLI:", result.Output);
        Assert.Contains("NSmithy: Smithy CLI failed with exit code 1.", result.Output);
    }

    private static async Task<ProcessResult> RunDotnetBuildAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments
    )
    {
        var output = new StringBuilder();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, output.ToString());
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NSmithy.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the smithy-dotnet repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
