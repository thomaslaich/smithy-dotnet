using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NSmithy.Tests.MSBuild;

/// <summary>Runs the dotnet CLI, for tests that build projects with the real MSBuild.</summary>
internal static class DotnetProcess
{
    public static async Task<DotnetResult> RunAsync(
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
        return new DotnetResult(process.ExitCode, plain);
    }

    public static string FindRepoRoot()
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

    /// <summary>
    /// The configuration this test assembly was built in, which is also where the in-repo MSBuild
    /// targets find the NSmithy.MSBuild tasks (packages/NSmithy.MSBuild/bin/{Configuration}).
    /// </summary>
    public static string Configuration =>
        AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal
        )
            ? "Debug"
            : "Release";
}

internal sealed record DotnetResult(int ExitCode, string Output);
