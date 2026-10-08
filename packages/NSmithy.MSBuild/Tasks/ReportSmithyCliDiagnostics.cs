using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>
/// Reports a failed <c>smithy build</c> as MSBuild errors. Validation events at ERROR or DANGER
/// severity (read from <c>smithy validate --format csv</c>) become errors carrying the event id
/// and source location, so they show up in every logger (including the terminal logger, which
/// hides plain build messages) and in IDE error lists. Failures without such events, such as a
/// plugin exception, are reported as a single error that includes the tail of the build output.
/// </summary>
public sealed partial class ReportSmithyCliDiagnostics : MsBuildTask
{
    private const int MaxFallbackLines = 20;

    /// <summary>The CSV written by <c>smithy validate --format csv</c>; may not exist.</summary>
    public string? ValidationEventsFile { get; set; }

    /// <summary>Lines <c>smithy build</c> wrote to stdout and stderr.</summary>
    public ITaskItem[] BuildOutput { get; set; } = [];

    /// <summary>Exit code of <c>smithy build</c>.</summary>
    [Required]
    public int ExitCode { get; set; }

    /// <summary>The build command that was run, included in the fallback error.</summary>
    [Required]
    public string Command { get; set; } = "";

    /// <summary>Directory the CLI ran in; relative event paths resolve against it.</summary>
    [Required]
    public string WorkingDirectory { get; set; } = "";

    public override bool Execute()
    {
        var errors = ReadValidationEvents().Where(e => e.Severity is "ERROR" or "DANGER").ToList();

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
            var tail = BuildOutput
                .Select(item => AnsiPattern().Replace(item.ItemSpec, ""))
                .Where(line => !string.IsNullOrWhiteSpace(line))
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

    private IReadOnlyList<SmithyValidationEvent> ReadValidationEvents()
    {
        if (string.IsNullOrEmpty(ValidationEventsFile) || !File.Exists(ValidationEventsFile))
            return [];
        return SmithyValidationEvents.ParseCsv(File.ReadAllText(ValidationEventsFile));
    }

    private static string FormatMessage(SmithyValidationEvent error)
    {
        var message = error.Message;
        if (error.File is null && error.Shape is not null)
            message += $" (shape: {error.Shape})";
        if (error.Hint is not null)
            message += $" Hint: {error.Hint}";
        return message;
    }

    private string? ResolvePath(string? path)
    {
        // Models loaded from JARs are reported as jar:file:...!/... URLs; keep those verbatim.
        if (path is null || path.Contains("!/", StringComparison.Ordinal))
            return path;
        return Path.GetFullPath(Path.Combine(WorkingDirectory, path));
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiPattern();
}
