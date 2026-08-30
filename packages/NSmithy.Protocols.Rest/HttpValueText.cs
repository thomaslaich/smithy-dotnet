using System.Globalization;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

/// <summary>Text forms of HTTP-bound values: what a label, query parameter or header carries.</summary>
internal static class HttpValueText
{
    internal static string FormatFloat(float value)
    {
        if (float.IsNaN(value))
        {
            return "NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    internal static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    internal static float ParseFloat(string value) =>
        value switch
        {
            "NaN" => float.NaN,
            "Infinity" => float.PositiveInfinity,
            "-Infinity" => float.NegativeInfinity,
            _ => float.Parse(value, CultureInfo.InvariantCulture),
        };

    internal static double ParseDouble(string value) =>
        value switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => double.Parse(value, CultureInfo.InvariantCulture),
        };

    internal static string FormatTimestamp(string format, DateTimeOffset value) =>
        format switch
        {
            "epoch-seconds" => FormatEpochSeconds(value),
            "http-date" => value
                .ToUniversalTime()
                .ToString("ddd, dd MMM yyyy HH':'mm':'ss 'GMT'", CultureInfo.InvariantCulture),
            "date-time" => FormatRfc3339(value),
            _ => throw new NotSupportedException($"Timestamp format '{format}' is not supported."),
        };

    internal static DateTimeOffset ParseTimestamp(string format, string value) =>
        format switch
        {
            "epoch-seconds" => ParseEpochSeconds(value),
            "http-date" => DateTimeOffset.ParseExact(value, "r", CultureInfo.InvariantCulture),
            // A label, query parameter, or header is only ever read from a request, so there is no
            // looser peer to accommodate here the way there is in a response body.
            "date-time" => Rfc3339.Parse(value, WireReadMode.Strict),
            _ => throw new NotSupportedException($"Timestamp format '{format}' is not supported."),
        };

    private static string FormatRfc3339(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseEpochSeconds(string value)
    {
        var seconds = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        var wholeSeconds = decimal.Truncate(seconds);
        var fractionalSeconds = seconds - wholeSeconds;
        return DateTimeOffset
            .FromUnixTimeSeconds((long)wholeSeconds)
            .AddTicks((long)(fractionalSeconds * TimeSpan.TicksPerSecond));
    }

    private static string FormatEpochSeconds(DateTimeOffset value)
    {
        var unixSeconds = value.ToUnixTimeSeconds();
        var fractionalTicks = value.ToUniversalTime().Ticks % TimeSpan.TicksPerSecond;
        if (fractionalTicks == 0)
        {
            return unixSeconds.ToString(CultureInfo.InvariantCulture);
        }

        var fractional = ((decimal)fractionalTicks / TimeSpan.TicksPerSecond).ToString(
            "0.################",
            CultureInfo.InvariantCulture
        );
        return $"{unixSeconds}{fractional[1..]}";
    }

    internal static IEnumerable<string> SplitHeaderList(string value, ShapeKind elementKind)
    {
        if (
            elementKind == ShapeKind.Timestamp
            && value.Contains("GMT,", StringComparison.OrdinalIgnoreCase)
        )
        {
            var segments = value.Split("GMT,", StringSplitOptions.None);
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i].Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                yield return i < segments.Length - 1 ? segment + " GMT" : segment;
            }

            yield break;
        }

        if (
            elementKind is ShapeKind.String or ShapeKind.Enum
            && value.Contains('"', StringComparison.Ordinal)
        )
        {
            foreach (var part in ParseQuotedHeaderList(value))
            {
                yield return part;
            }

            yield break;
        }

        foreach (var part in value.Split(','))
        {
            yield return part.Trim();
        }
    }

    private static IEnumerable<string> ParseQuotedHeaderList(string value)
    {
        var builder = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        foreach (var ch in value)
        {
            if (escaping)
            {
                builder.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\' && inQuotes)
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                yield return builder.ToString().Trim();
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        if (builder.Length > 0 || (value.Length > 0 && value[^1] == ','))
        {
            yield return builder.ToString().Trim();
        }
    }

    /// <summary>A header list element, quoted when a bare one would be split or misread.</summary>
    internal static string QuoteHeaderListElement(string value) =>
        value.Length == 0
        || value.Any(ch => ch == ',' || ch == '"' || ch == '\\' || char.IsWhiteSpace(ch))
            ? "\""
                + value
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal)
                + "\""
            : value;

    internal static string EscapeGreedyLabel(string value) =>
        string.Join("/", value.Split('/').Select(Uri.EscapeDataString));
}

/// <summary>A request URI under construction; query pairs are appended in the order written.</summary>
internal sealed class HttpUriBuilder(string template)
{
    private readonly StringBuilder uri = new(template);
    private bool hasQuery = template.Contains('?', StringComparison.Ordinal);

    internal void ReplaceLabel(string placeholder, string value) => uri.Replace(placeholder, value);

    internal void AppendQuery(string name, string value)
    {
        uri.Append(hasQuery ? '&' : '?');
        hasQuery = true;
        uri.Append(Uri.EscapeDataString(name));
        uri.Append('=');
        uri.Append(Uri.EscapeDataString(value));
    }

    public override string ToString() => uri.ToString();
}
