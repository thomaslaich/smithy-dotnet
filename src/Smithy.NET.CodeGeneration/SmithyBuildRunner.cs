using Smithy.NET.CodeGeneration.Model;

namespace Smithy.NET.CodeGeneration;

public sealed class SmithyBuildRunner
{
    private readonly ISmithyCliRunner runner;

    public SmithyBuildRunner()
        : this(new ProcessSmithyCliRunner()) { }

    internal SmithyBuildRunner(ISmithyCliRunner runner)
    {
        this.runner = runner;
    }

    public async ValueTask<SmithyBuildResult> BuildAsync(
        SmithyBuildOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        var smithyPath = ResolveSmithyPath(options.SmithyCliPath);
        var arguments = CreateArguments(options);
        var result = await runner
            .RunAsync(
                new SmithyCliInvocation(smithyPath, arguments, options.WorkingDirectory),
                cancellationToken
            )
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw CreateBuildFailureException(result);
        }

        var projection = options.Projection ?? "source";
        var modelPath = Path.Combine(options.OutputDirectory, projection, "model", "model.json");
        if (!File.Exists(modelPath))
        {
            throw new SmithyException(
                $"Smithy CLI completed but did not write the expected model JSON AST at '{modelPath}'."
            );
        }

        await using var modelStream = File.OpenRead(modelPath);
        var model = await SmithyJsonAstReader
            .ReadAsync(modelStream, cancellationToken)
            .ConfigureAwait(false);
        return new SmithyBuildResult(modelPath, model);
    }

    private static string ResolveSmithyPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            throw CreateMissingCliException();
        }

        var executableName = OperatingSystem.IsWindows() ? "smithy.exe" : "smithy";
        foreach (
            var directory in pathVariable.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw CreateMissingCliException();
    }

    private static IReadOnlyList<string> CreateArguments(SmithyBuildOptions options)
    {
        var arguments = new List<string>
        {
            "build",
            "--config",
            options.BuildFile,
            "--output",
            options.OutputDirectory,
        };

        if (!string.IsNullOrWhiteSpace(options.Projection))
        {
            arguments.Add("--projection");
            arguments.Add(options.Projection);
        }

        return arguments;
    }

    private static SmithyException CreateBuildFailureException(SmithyCliRunResult result)
    {
        var message = result.StandardError.Contains("java", StringComparison.OrdinalIgnoreCase)
            ? "Smithy CLI failed. Verify that Java is installed and available to the Smithy CLI."
            : "Smithy CLI validation or build failed.";

        return new SmithyException(
            $"{message} Exit code {result.ExitCode}:{Environment.NewLine}{result.StandardError}"
        );
    }

    private static SmithyException CreateMissingCliException()
    {
        return new SmithyException(
            "Smithy CLI was not found. Install the Smithy CLI, ensure Java is available, or set SmithyCliPath to an executable Smithy CLI path."
        );
    }
}
