using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NSmithy.Tests.MSBuild;

public sealed class SmithyCliDiagnosticsTests
{
    [Fact]
    public async Task GenerateSmithyCodeReportsCliFailureOutput()
    {
        var result = await GenerateSmithyCodeAsync("--definitely-not-a-smithy-flag");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error NSMITHYCLI:", result.Output);
        Assert.Contains("NSmithy: Smithy CLI failed with exit code 1.", result.Output);
        Assert.Contains("Unexpected CLI argument: --definitely-not-a-smithy-flag", result.Output);
    }

    [Fact]
    public async Task GenerateSmithyCodeReportsValidationErrorsWithLocation()
    {
        var model = Path.Combine(Path.GetTempPath(), $"nsmithy-invalid-{Guid.NewGuid():N}.smithy");
        await File.WriteAllTextAsync(
            model,
            """
            $version: "2"
            namespace nsmithy.invalid
            @aws.protocols#rpcv2Cbor
            structure Invalid {}
            """
        );

        try
        {
            var result = await GenerateSmithyCodeAsync($"\"{model}\"");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains($"{model}(3,1): Smithy error Model.UnresolvedTrait:", result.Output);
            Assert.Contains("Unable to resolve trait `aws.protocols#rpcv2Cbor`.", result.Output);
            Assert.Contains("3| @aws.protocols#rpcv2Cbor", result.Output);
            Assert.DoesNotContain("NSMITHYCLI", result.Output);
        }
        finally
        {
            File.Delete(model);
        }
    }

    // Forces the terminal logger, which (unlike the console logger) hides the CLI's own output,
    // so only what NSmithy reports as errors is visible.
    private static Task<ProcessResult> GenerateSmithyCodeAsync(string smithyExtraArgs)
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

        return RunDotnetBuildAsync(
            repoRoot,
            [
                "build",
                projectPath,
                "--configuration",
                "Release",
                "--no-restore",
                "-tl:on",
                "/t:GenerateSmithyCode",
                $"/p:SmithyStampFile={stampFile}",
                $"/p:SmithyExtraArgs={smithyExtraArgs}",
            ]
        );
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

        var plain = Regex.Replace(output.ToString(), @"\x1B\[[0-9;?]*[A-Za-z]", "");
        return new ProcessResult(process.ExitCode, plain);
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
