using System.Globalization;
using System.Text.RegularExpressions;

namespace NSmithy.Core.Serde;

/// <summary>
/// Smithy's <c>date-time</c> timestamp format: the <c>date-time</c> production of
/// <see href="https://www.rfc-editor.org/rfc/rfc3339#section-5.6">RFC 3339 section 5.6</see>.
/// </summary>
/// <remarks>
/// <see cref="DateTimeOffset.Parse(string, IFormatProvider?, DateTimeStyles)"/> is far more
/// permissive than that production — it also accepts IMF-fixdate, which is a different Smithy format
/// — so a server using it would silently accept a timestamp the model says is wrong. The shape is
/// checked first and only then handed to the framework parser, which is the part that knows about
/// leap years and offsets.
/// </remarks>
public static partial class Rfc3339
{
    /// <summary>
    /// Reads a <c>date-time</c>. Smithy's production carries no UTC offset — every conforming peer
    /// writes <c>Z</c> — so a <see cref="WireReadMode.Strict"/> read rejects one and a
    /// <see cref="WireReadMode.Lenient"/> read honors it.
    /// </summary>
    public static DateTimeOffset Parse(string value, WireReadMode readMode = WireReadMode.Lenient)
    {
        ArgumentNullException.ThrowIfNull(value);
        var match = Pattern().Match(value);
        if (!match.Success)
        {
            throw new FormatException($"'{value}' is not an RFC 3339 date-time.");
        }

        if (readMode == WireReadMode.Strict && match.Groups["offset"].Value is not ("Z" or "z"))
        {
            throw new FormatException(
                $"'{value}' carries a UTC offset, which a date-time timestamp does not."
            );
        }

        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind
        );
    }

    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(\.\d+)?(?<offset>[Zz]|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex Pattern();
}
