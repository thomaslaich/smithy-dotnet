using System.Collections;
using System.Globalization;
using System.Text;
using SmithyNet.Codecs;

namespace SmithyNet.Client.RestJson;

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

    public static T DeserializeBody<T>(ISmithyPayloadCodec codec, byte[] content)
    {
        return content.Length == 0 ? default! : codec.Deserialize<T>(content);
    }

    public static T DeserializeRequiredBody<T>(ISmithyPayloadCodec codec, byte[] content)
    {
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Response body is required but was empty.");
        }

        return codec.Deserialize<T>(content);
    }

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
            ? ConvertHttpValue<T>(values[0])
            : throw new InvalidOperationException(
                $"Required response header '{name}' was missing."
            );
    }

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

        return values;
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

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateStringMap(object value)
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

    private static T CreateStringMap<T>(Dictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            return default!;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType.IsAssignableFrom(values.GetType()))
        {
            return (T)(object)values;
        }

        var constructor = targetType.GetConstructor([typeof(IReadOnlyDictionary<string, string>)]);
        constructor ??= targetType.GetConstructor([typeof(Dictionary<string, string>)]);
        return constructor is not null
            ? (T)constructor.Invoke([values])
            : throw new InvalidOperationException($"Cannot create string map type '{targetType}'.");
    }

    private static T ConvertHttpValue<T>(string value)
    {
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(string))
        {
            return (T)(object)value;
        }

        if (targetType.IsEnum)
        {
            if (!Enum.TryParse(targetType, value, ignoreCase: false, out var enumResult))
            {
                throw new InvalidOperationException(
                    $"Unknown enum value '{value}' for type '{targetType.Name}'."
                );
            }

            return (T)enumResult!;
        }

        if (targetType == typeof(DateTimeOffset))
        {
            if (
                long.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var epochSeconds
                )
            )
            {
                return (T)(object)DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
            }

            return (T)(object)DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        }

        var constructor = targetType.GetConstructor([typeof(string)]);
        if (constructor is not null)
        {
            return (T)constructor.Invoke([value]);
        }

        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }
}
