using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

/// <summary>
/// Smithy timestamp wire formats for JSON bodies. The body default is <c>epoch-seconds</c>;
/// <c>@timestampFormat</c> on the member or target shape overrides it.
/// </summary>
internal static class TimestampFormat
{
    private static readonly ShapeId TimestampFormatTrait = new("smithy.api", "timestampFormat");

    public static string Resolve(IReadOnlyDictionary<ShapeId, Trait>? memberTraits, Schema schema)
    {
        if (
            memberTraits is not null
            && memberTraits.TryGetValue(TimestampFormatTrait, out var memberTrait)
        )
        {
            return memberTrait.Value.AsString();
        }

        if (schema.Resolved.Traits.TryGetValue(TimestampFormatTrait, out var schemaTrait))
        {
            return schemaTrait.Value.AsString();
        }

        return "epoch-seconds";
    }

    public static void Write(Utf8JsonWriter writer, DateTimeOffset value, string format)
    {
        switch (format)
        {
            case "epoch-seconds":
                var utcTicks = value.ToUniversalTime().Ticks;
                if (utcTicks % TimeSpan.TicksPerSecond == 0)
                {
                    writer.WriteNumberValue(value.ToUnixTimeSeconds());
                }
                else
                {
                    writer.WriteNumberValue(value.ToUnixTimeMilliseconds() / 1000.0m);
                }

                break;
            case "http-date":
                writer.WriteStringValue(
                    value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture)
                );
                break;
            default: // date-time (RFC3339)
                var utc = value.ToUniversalTime();
                var text =
                    utc.Ticks % TimeSpan.TicksPerSecond == 0
                        ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                        : utc.ToString(
                            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                            CultureInfo.InvariantCulture
                        );
                writer.WriteStringValue(text);
                break;
        }
    }

    public static DateTimeOffset Read(JsonElement value, string format) =>
        format switch
        {
            "epoch-seconds" => DateTimeOffset.FromUnixTimeMilliseconds(
                (long)(value.GetDouble() * 1000)
            ),
            "http-date" => DateTimeOffset.ParseExact(
                value.GetString()!,
                "r",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None
            ),
            _ => DateTimeOffset.Parse(
                value.GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
        };
}
