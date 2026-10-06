using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>
/// Turns the output of a failed <c>smithy build</c> into MSBuild errors. Smithy validation
/// events at ERROR or DANGER severity become errors carrying the event id, source location and
/// source excerpt, so they show up in every logger (including the terminal logger, which hides plain build
/// messages) and in IDE error lists. Failures that produce no such event are reported as a
/// single error that includes the tail of the CLI output.
/// </summary>
public sealed class ReportSmithyCliDiagnostics : MsBuildTask
{
    private const int MaxFallbackLines = 20;

    /// <summary>Lines the Smithy CLI wrote to stdout and stderr.</summary>
    public ITaskItem[] Output { get; set; } = [];

    /// <summary>Exit code of the Smithy CLI.</summary>
    [Required]
    public int ExitCode { get; set; }

    /// <summary>The command that was run, included in the fallback error.</summary>
    [Required]
    public string Command { get; set; } = "";

    /// <summary>Directory the CLI ran in; relative event paths resolve against it.</summary>
    [Required]
    public string WorkingDirectory { get; set; } = "";

    public override bool Execute()
    {
        var lines = Output.Select(item => item.ItemSpec).ToList();
        var errors = SmithyCliOutput
            .Parse(lines)
            .Where(e => e.Severity is "ERROR" or "DANGER")
            .ToList();

        foreach (var error in errors)
        {
            Log.LogError(
                subcategory: "Smithy",
                errorCode: error.EventId,
                helpKeyword: null,
                file: ResolvePath(error.File),
                lineNumber: error.Line,
                columnNumber: error.Column,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: FormatMessage(error)
            );
        }

        if (errors.Count == 0)
        {
            var tail = lines
                .Select(SmithyCliOutput.StripAnsi)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .TakeLast(MaxFallbackLines);
            Log.LogError(
                subcategory: null,
                errorCode: "NSMITHYCLI",
                helpKeyword: null,
                file: null,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: $"NSmithy: Smithy CLI failed with exit code {ExitCode}.{Environment.NewLine}"
                    + string.Join(Environment.NewLine, tail)
                    + $"{Environment.NewLine}Command: {Command}"
            );
        }

        return false;
    }

    private static string FormatMessage(SmithyValidationEvent error)
    {
        var message =
            error.File is null && error.Shape is not null
                ? $"{error.Message} (shape: {error.Shape})"
                : error.Message;
        return error.Frame is null ? message : $"{message}\n{error.Frame}";
    }

    private string? ResolvePath(string? path)
    {
        // Models loaded from JARs are reported as jar:file:...!/... URLs; keep those verbatim.
        if (path is null || path.Contains("!/", StringComparison.Ordinal))
            return path;
        return Path.GetFullPath(Path.Combine(WorkingDirectory, path));
    }
}
