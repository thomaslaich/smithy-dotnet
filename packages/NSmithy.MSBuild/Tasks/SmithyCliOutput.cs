using System.Globalization;
using System.Text.RegularExpressions;

namespace NSmithy.Contracts;

/// <summary>Reads the human-readable output of the Smithy CLI.</summary>
public static partial class SmithyCliOutput
{
    /// <summary>
    /// Parses the validation events in Smithy CLI text output. Each event is a header line
    /// (<c>── ERROR ─── Event.Id</c>), optional <c>Shape:</c> and <c>File:</c> lines, an optional
    /// source frame, and a message wrapped at the terminal width.
    /// </summary>
    public static IReadOnlyList<SmithyValidationEvent> Parse(IEnumerable<string> lines)
    {
        var events = new List<SmithyValidationEvent>();
        SmithyValidationEvent? current = null;
        var message = new List<string>();
        var frame = new List<string>();

        void Flush()
        {
            if (current is not null)
            {
                events.Add(
                    current with
                    {
                        Message = string.Join(" ", message),
                        Frame = frame.Count == 0 ? null : AlignFrame(frame),
                    }
                );
            }
            current = null;
            message.Clear();
            frame.Clear();
        }

        foreach (var raw in lines)
        {
            var line = StripAnsi(raw).Trim();

            var header = HeaderPattern().Match(line);
            if (header.Success)
            {
                Flush();
                current = new SmithyValidationEvent(
                    header.Groups["severity"].Value,
                    header.Groups["id"].Value,
                    Message: ""
                );
                continue;
            }

            if (current is null || line.Length == 0)
                continue;

            if (message.Count == 0 && SourceFramePattern().IsMatch(line))
            {
                frame.Add(line);
                continue;
            }

            if (SummaryPattern().IsMatch(line))
            {
                Flush();
                continue;
            }

            if (message.Count == 0 && line.StartsWith("Shape:", StringComparison.Ordinal))
            {
                current = current with { Shape = line["Shape:".Length..].Trim() };
                continue;
            }

            var file = FilePattern().Match(line);
            if (message.Count == 0 && file.Success)
            {
                current = current with
                {
                    File = file.Groups["path"].Value,
                    Line = int.Parse(file.Groups["line"].Value, CultureInfo.InvariantCulture),
                    Column = int.Parse(file.Groups["column"].Value, CultureInfo.InvariantCulture),
                };
                continue;
            }

            message.Add(line);
        }

        Flush();
        return events;
    }

    // Re-pads the gutter so the '|' separators line up, whatever whitespace survived capture.
    private static string AlignFrame(List<string> frame)
    {
        var rows = frame
            .Select(l => l.Split('|', 2))
            .Select(parts => (Gutter: parts[0].Trim(), Code: parts[1].TrimEnd()))
            .ToList();
        var width = rows.Max(r => r.Gutter.Length);
        return string.Join("\n", rows.Select(r => $"{r.Gutter.PadLeft(width)}|{r.Code}"));
    }

    internal static string StripAnsi(string line) => AnsiPattern().Replace(line, "");

    // The rule characters are U+2500; matched loosely in case the console mis-decodes them.
    [GeneratedRegex(
        @"^\S+\s+(?<severity>ERROR|DANGER|WARNING|NOTE|SUPPRESSED)\s+\S+\s+(?<id>[\w.#$-]+)$"
    )]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^File:\s+(?<path>.+):(?<line>\d+):(?<column>\d+)$")]
    private static partial Regex FilePattern();

    // "12| @rpcv2Cbor", "  | ^", "··|"
    [GeneratedRegex(@"^(\d+|[^\w\s]+)?\s*\|")]
    private static partial Regex SourceFramePattern();

    [GeneratedRegex(@"^(SUCCESS|FAILURE): Validated \d+ shapes")]
    private static partial Regex SummaryPattern();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiPattern();
}

/// <summary>A validation event parsed from Smithy CLI output.</summary>
public sealed record SmithyValidationEvent(
    string Severity,
    string EventId,
    string Message,
    string? Shape = null,
    string? File = null,
    int Line = 0,
    int Column = 0,
    string? Frame = null
);
