using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.RestJson;

public static class RestJsonClientProtocol
{
    public static void AddHeader(
        IDictionary<string, IReadOnlyList<string>> headers,
        string name,
        object? value
    )
    {
        if (value is null)
        {
            return;
        }

        headers[name] = [FormatHttpValue(value)];
    }

    public static void AddPrefixedHeaders(
        IDictionary<string, IReadOnlyList<string>> headers,
        string prefix,
        object? value
    )
    {
        if (value is null)
        {
            return;
        }

        foreach (var item in EnumerateStringMap(value))
        {
            if (item.Value is null)
            {
                continue;
            }

            headers[$"{prefix}{item.Key}"] = [FormatHttpValue(item.Value)];
        }
    }

    public static void AppendQuery(StringBuilder builder, string name, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is IEnumerable values && value is not string)
        {
            foreach (var item in values)
            {
                AppendQueryValue(builder, name, item);
            }

            return;
        }

        AppendQueryValue(builder, name, value);
    }

    public static void AppendQueryMap(StringBuilder builder, object? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (var item in EnumerateStringMap(value))
        {
            AppendQueryValue(builder, item.Key, item.Value);
        }
    }

    public static string EscapeGreedyLabel(object value)
    {
        return string.Join("/", FormatHttpValue(value).Split('/').Select(Uri.EscapeDataString));
    }

    public static T DeserializeBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T>
    {
        return content.Length == 0 ? default! : codec.Deserialize<T>(content);
    }

    public static T DeserializeBody<T>(
        ISmithyCodec codec,
        byte[] content,
        Func<IShapeDeserializer, T> read
    )
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(read);
        return content.Length == 0 ? default! : codec.Deserialize(content, read);
    }

    public static T DeserializeRequiredBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T>
    {
        if (content.Length == 0)
        {
            return Activator.CreateInstance<T>();
        }

        return codec.Deserialize<T>(content);
    }

    public static T DeserializeRequiredBody<T>(
        ISmithyCodec codec,
        byte[] content,
        Func<IShapeDeserializer, T> read
    )
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(read);
        if (content.Length == 0)
        {
            return Activator.CreateInstance<T>();
        }

        return codec.Deserialize(content, read);
    }

    [return: MaybeNull]
    public static T GetHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        return headers.TryGetValue(name, out var values) && values.Count > 0
            ? ConvertHttpValue<T>(values[0])
            : default!;
    }

    public static T GetRequiredHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        return headers.TryGetValue(name, out var values) && values.Count > 0
            ? ConvertHttpValue<T>(values[0])!
            : throw new InvalidOperationException(
                $"Required response header '{name}' was missing."
            );
    }

    [return: MaybeNull]
    public static T GetPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    )
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            if (
                !header.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || header.Value.Count == 0
            )
            {
                continue;
            }

            values[header.Key[prefix.Length..]] = header.Value[0];
        }

        return CreateStringMap<T>(values);
    }

    public static T GetRequiredPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    )
    {
        var values = GetPrefixedHeaders<T>(headers, prefix);
        if (EqualityComparer<T>.Default.Equals(values, default!))
        {
            throw new InvalidOperationException(
                $"Required prefixed response headers '{prefix}' were missing."
            );
        }

        return values!;
    }

    [return: MaybeNull]
    public static T ConvertHttpValue<T>(string? value)
    {
        if (value is null)
        {
            return default;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(string))
        {
            return (T)(object)value;
        }

        if (targetType.IsEnum)
        {
            return (T)
                Enum.ToObject(
                    targetType,
                    Convert.ChangeType(
                        value,
                        Enum.GetUnderlyingType(targetType),
                        CultureInfo.InvariantCulture
                    )!
                );
        }

        var constructor = targetType.GetConstructor([typeof(string)]);
        if (constructor is not null)
        {
            return (T)constructor.Invoke([value]);
        }

        if (targetType == typeof(DateTimeOffset))
        {
            if (
                decimal.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var epochSeconds
                )
            )
            {
                var wholeSeconds = decimal.Truncate(epochSeconds);
                var fractionalSeconds = epochSeconds - wholeSeconds;
                var ticks = (long)(fractionalSeconds * TimeSpan.TicksPerSecond);
                return (T)
                    (object)
                        new DateTimeOffset(
                            DateTime.UnixEpoch.AddSeconds((double)wholeSeconds).AddTicks(ticks),
                            TimeSpan.Zero
                        );
            }

            return (T)
                (object)
                    DateTimeOffset.Parse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind
                    );
        }

        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    [return: MaybeNull]
    public static T CreateStringMap<T>(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            return default;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType.IsAssignableFrom(values.GetType()))
        {
            return (T)(object)new Dictionary<string, string>(values, StringComparer.Ordinal);
        }

        var constructor = targetType.GetConstructor([typeof(IReadOnlyDictionary<string, string>)]);
        constructor ??= targetType.GetConstructor([typeof(Dictionary<string, string>)]);
        return constructor is not null
            ? (T)
                constructor.Invoke([new Dictionary<string, string>(values, StringComparer.Ordinal)])
            : throw new InvalidOperationException($"Cannot create string map type '{targetType}'.");
    }

    public static IEnumerable<KeyValuePair<string, object?>> EnumerateStringMap(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return EnumerateStringMapCore(value);
    }

    private static void AppendQueryValue(StringBuilder builder, string name, object? value)
    {
        if (value is null)
        {
            return;
        }

        builder.Append(builder.ToString().Contains('?') ? '&' : '?');
        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(FormatHttpValue(value)));
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateStringMapCore(object value)
    {
        var values =
            value is IDictionary ? value : value.GetType().GetProperty("Values")?.GetValue(value);
        if (values is not IEnumerable enumerable)
        {
            yield break;
        }

        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            if (item is DictionaryEntry dictionaryEntry)
            {
                if (dictionaryEntry.Key is not null)
                {
                    yield return new KeyValuePair<string, object?>(
                        dictionaryEntry.Key.ToString() ?? string.Empty,
                        dictionaryEntry.Value
                    );
                }

                continue;
            }

            var itemType = item.GetType();
            var key = itemType.GetProperty("Key")?.GetValue(item)?.ToString();
            if (key is null)
            {
                continue;
            }

            yield return new KeyValuePair<string, object?>(
                key,
                itemType.GetProperty("Value")?.GetValue(item)
            );
        }
    }

    public static string FormatHttpValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            DateTimeOffset timestamp => timestamp
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            Enum enumValue => Convert
                .ToInt32(enumValue, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
                ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }
}
