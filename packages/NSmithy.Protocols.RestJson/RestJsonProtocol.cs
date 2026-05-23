using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.RestJson;

public static class RestJsonProtocol
{
    private static readonly ShapeId TimestampFormatTraitId = ShapeId.Parse(
        "smithy.api#timestampFormat"
    );
    private static readonly ShapeId HttpHeaderTraitId = ShapeId.Parse("smithy.api#httpHeader");
    private static readonly ShapeId HttpLabelTraitId = ShapeId.Parse("smithy.api#httpLabel");
    private static readonly ShapeId HttpQueryTraitId = ShapeId.Parse("smithy.api#httpQuery");
    private static readonly ShapeId HttpQueryParamsTraitId = ShapeId.Parse(
        "smithy.api#httpQueryParams"
    );
    private static readonly ShapeId MediaTypeTraitId = ShapeId.Parse("smithy.api#mediaType");

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

        // Smithy list shapes (e.g. BooleanList, StringList) are wrapper types with a
        // Values property of IReadOnlyList<T> — not IEnumerable — so we expand them here.
        var listValues = TryGetSmithyListValues(value);
        if (listValues is not null)
        {
            headers[name] = [string.Join(", ", listValues.Select(v => FormatHttpValue(v!)))];
            return;
        }

        headers[name] = [FormatHttpValue(value)];
    }

    public static void AddHeader(
        IDictionary<string, IReadOnlyList<string>> headers,
        string name,
        Schema schema,
        object? value
    )
    {
        if (value is null)
        {
            return;
        }

        var listValues = TryGetSmithyListValues(value);
        if (listValues is not null)
        {
            headers[name] = [string.Join(", ", listValues.Select(v => FormatHttpValue(schema, v!)))];
            return;
        }

        headers[name] = [FormatHttpValue(schema, value)];
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

        // Smithy list shapes are wrapper types with a Values IReadOnlyList<T>, not IEnumerable.
        var listValues = TryGetSmithyListValues(value);
        if (listValues is not null)
        {
            foreach (var item in listValues)
            {
                if (item is not null)
                    AppendQueryValue(builder, name, item);
            }

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

    public static void AppendQuery(StringBuilder builder, string name, Schema schema, object? value)
    {
        if (value is null)
        {
            return;
        }

        var listValues = TryGetSmithyListValues(value);
        if (listValues is not null)
        {
            foreach (var item in listValues)
            {
                if (item is not null)
                    AppendQueryValue(builder, name, schema, item);
            }

            return;
        }

        if (value is IEnumerable values && value is not string)
        {
            foreach (var item in values)
            {
                AppendQueryValue(builder, name, schema, item);
            }

            return;
        }

        AppendQueryValue(builder, name, schema, value);
    }

    public static void AppendQueryMap(StringBuilder builder, object? value)
    {
        AppendQueryMap(builder, value, excludedNames: null);
    }

    public static void AppendQueryMap(
        StringBuilder builder,
        object? value,
        IEnumerable<string>? excludedNames
    )
    {
        if (value is null)
        {
            return;
        }

        HashSet<string>? excluded = null;
        if (excludedNames is not null)
        {
            excluded = new HashSet<string>(excludedNames, StringComparer.Ordinal);
        }

        foreach (var item in EnumerateStringMap(value))
        {
            if (item.Value is null)
                continue;
            if (excluded?.Contains(item.Key) == true)
                continue;

            // Map values may be Smithy list shapes (e.g. StringListMap has StringList values).
            var listValues = TryGetSmithyListValues(item.Value);
            if (listValues is not null)
            {
                foreach (var element in listValues)
                {
                    if (element is not null)
                        AppendQueryValue(builder, item.Key, element);
                }
                continue;
            }

            if (item.Value is IEnumerable seq && item.Value is not string)
            {
                foreach (var element in seq)
                {
                    if (element is not null)
                        AppendQueryValue(builder, item.Key, element);
                }
                continue;
            }

            AppendQueryValue(builder, item.Key, item.Value);
        }
    }

    public static string EscapeGreedyLabel(object value)
    {
        return string.Join("/", FormatHttpValue(value).Split('/').Select(Uri.EscapeDataString));
    }

    public static string EscapeGreedyLabel(Schema schema, object value)
    {
        return string.Join(
            "/",
            FormatHttpValue(schema, value).Split('/').Select(Uri.EscapeDataString)
        );
    }

    public static string? DeserializeErrorType(SmithyHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var headerValue =
            TryGetFirstHeaderValue(response.Headers, "X-Amzn-Errortype")
            ?? TryGetFirstHeaderValue(response.ContentHeaders, "X-Amzn-Errortype");
        if (!string.IsNullOrWhiteSpace(headerValue))
            return NormalizeErrorType(headerValue);

        if (response.Content.Length == 0)
            return null;

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (
                document.RootElement.TryGetProperty("__type", out var dunderType)
                && dunderType.ValueKind == JsonValueKind.String
            )
                return NormalizeErrorType(dunderType.GetString());

            if (
                document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
            )
                return NormalizeErrorType(code.GetString());
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
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

    [return: MaybeNull]
    public static T GetHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name,
        Schema schema
    )
    {
        return headers.TryGetValue(name, out var values) && values.Count > 0
            ? ConvertHttpValue<T>(schema, values[0])
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

    public static T GetRequiredHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name,
        Schema schema
    )
    {
        return headers.TryGetValue(name, out var values) && values.Count > 0
            ? ConvertHttpValue<T>(schema, values[0])!
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
        return ConvertHttpValue<T>(schema: null, value);
    }

    [return: MaybeNull]
    public static T ConvertHttpValue<T>(Schema? schema, string? value)
    {
        if (value is null)
        {
            return default;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType == typeof(string))
        {
            if (HasMediaType(schema))
            {
                return (T)(object)Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
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
            return (T)(object)ParseTimestamp(schema, value);
        }

        // Smithy list wrapper types (e.g. BooleanList, StringList) have a single ctor that
        // takes IEnumerable<T>. Headers for these types are comma-separated.
        var smithyList = TryBuildSmithyListFromHeader(schema, targetType, value);
        if (smithyList is not null)
            return (T)smithyList;

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

    private static void AppendQueryValue(StringBuilder builder, string name, Schema schema, object? value)
    {
        if (value is null)
        {
            return;
        }

        builder.Append(builder.ToString().Contains('?') ? '&' : '?');
        builder.Append(Uri.EscapeDataString(name));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(FormatHttpValue(schema, value)));
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
            // bool must precede IFormattable to produce lowercase "true"/"false".
            bool b => b ? "true" : "false",
            // NaN/Infinity must precede IFormattable to produce the canonical strings.
            float f when float.IsNaN(f) => "NaN",
            float f when float.IsPositiveInfinity(f) => "Infinity",
            float f when float.IsNegativeInfinity(f) => "-Infinity",
            double d when double.IsNaN(d) => "NaN",
            double d when double.IsPositiveInfinity(d) => "Infinity",
            double d when double.IsNegativeInfinity(d) => "-Infinity",
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

    public static string FormatHttpValue(Schema schema, object value)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(value);

        if (value is string text && HasMediaType(schema))
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        if (value is DateTimeOffset timestamp)
            return FormatTimestamp(schema, timestamp);

        return FormatHttpValue(value);
    }

    /// <summary>
    /// Returns the items of a Smithy list wrapper shape (a type with a <c>Values</c> property
    /// of type <c>IReadOnlyList&lt;T&gt;</c>), or <c>null</c> if <paramref name="value"/> is
    /// not a Smithy list wrapper.
    /// </summary>
    private static IEnumerable<object?>? TryGetSmithyListValues(object value)
    {
        if (value is IEnumerable)
            return null; // plain enumerables are handled by the existing IEnumerable branch

        var valuesProp = value.GetType().GetProperty("Values");
        if (valuesProp is null)
            return null;

        var propType = valuesProp.PropertyType;
        // IReadOnlyList<T> has one generic arg; IReadOnlyDictionary<K,V> has two — skip maps.
        if (!propType.IsGenericType || propType.GetGenericArguments().Length != 1)
            return null;

        return (valuesProp.GetValue(value) as IEnumerable)?.Cast<object?>();
    }

    /// <summary>
    /// Converts a single HTTP value string to a target element type (used when building
    /// Smithy list/map shapes from comma-separated header values).
    /// </summary>
    private static object? ConvertHttpValueElement(Schema? schema, Type targetType, string value)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(string))
            return value;

        if (underlying == typeof(bool))
            return bool.Parse(value);

        if (underlying == typeof(float))
            return value switch
            {
                "NaN" => float.NaN,
                "Infinity" => float.PositiveInfinity,
                "-Infinity" => float.NegativeInfinity,
                _ => float.Parse(value, CultureInfo.InvariantCulture),
            };

        if (underlying == typeof(double))
            return value switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                _ => double.Parse(value, CultureInfo.InvariantCulture),
            };

        if (underlying.IsEnum)
            return Enum.ToObject(
                underlying,
                Convert.ChangeType(
                    value,
                    Enum.GetUnderlyingType(underlying),
                    CultureInfo.InvariantCulture
                )!
            );

        var stringCtor = underlying.GetConstructor([typeof(string)]);
        if (stringCtor is not null)
            return stringCtor.Invoke([value]);

        if (underlying == typeof(DateTimeOffset))
            return ParseTimestamp(schema, value);

        return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Tries to build a Smithy list wrapper from a comma-separated HTTP header string.
    /// Returns <c>null</c> if <paramref name="targetType"/> is not a Smithy list wrapper.
    /// </summary>
    private static object? TryBuildSmithyListFromHeader(Schema? schema, Type targetType, string value)
    {
        if (!targetType.IsClass)
            return null;

        var ctors = targetType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0)
            return null;

        var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
        var ps = ctor.GetParameters();
        if (ps.Length != 1)
            return null;

        var paramType = ps[0].ParameterType;
        if (!paramType.IsGenericType || paramType.GetGenericArguments().Length != 1)
            return null;

        var elementType = paramType.GetGenericArguments()[0];
        var parts = SplitHeaderList(value, elementType);
        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var part in parts)
            list.Add(ConvertHttpValueElement(schema, elementType, part));
        return ctor.Invoke([list]);
    }

    /// <summary>
    /// Splits a comma-separated HTTP header value into individual items. HTTP date values
    /// (e.g. "Mon, 16 Dec 2019 23:48:18 GMT") contain commas and must be split on the
    /// "GMT," boundary rather than a bare "," to avoid splitting within a date.
    /// </summary>
    private static IEnumerable<string> SplitHeaderList(string value, Type elementType)
    {
        // HTTP date timestamps end with " GMT" and the list separator is "GMT, ".
        if (elementType == typeof(DateTimeOffset) && value.Contains("GMT,", StringComparison.OrdinalIgnoreCase))
        {
            var segments = value.Split("GMT,", StringSplitOptions.None);
            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i].Trim();
                if (i < segments.Length - 1 && !string.IsNullOrEmpty(seg))
                    yield return seg + " GMT";
                else if (!string.IsNullOrEmpty(seg))
                    yield return seg;
            }
            yield break;
        }

        if (elementType == typeof(string) && value.Contains('"'))
        {
            foreach (var part in ParseQuotedHeaderList(value))
                yield return part;
            yield break;
        }

        foreach (var part in value.Split(','))
            yield return part.Trim();
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
            yield return builder.ToString().Trim();
    }

    private static string GetTimestampFormat(Schema? schema)
    {
        if (schema is null)
            return "date-time";

        var trait = schema.GetTrait(TimestampFormatTraitId) ?? schema.Target?.GetTrait(TimestampFormatTraitId);
        if (trait?.Value.AsString() is { } explicitFormat)
            return explicitFormat;

        if (schema.HasTrait(HttpHeaderTraitId))
            return "http-date";

        if (
            schema.HasTrait(HttpLabelTraitId)
            || schema.HasTrait(HttpQueryTraitId)
            || schema.HasTrait(HttpQueryParamsTraitId)
        )
            return "date-time";

        return "date-time";
    }

    private static bool HasMediaType(Schema? schema)
    {
        if (schema is null)
            return false;

        return schema.GetTrait(MediaTypeTraitId) is not null
            || schema.Target?.GetTrait(MediaTypeTraitId) is not null;
    }

    private static string FormatTimestamp(Schema schema, DateTimeOffset value)
    {
        return GetTimestampFormat(schema) switch
        {
            "epoch-seconds" => FormatEpochSeconds(value),
            "http-date" => value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture),
            _ => FormatDateTimeTimestamp(value),
        };
    }

    private static DateTimeOffset ParseTimestamp(Schema? schema, string value)
    {
        return GetTimestampFormat(schema) switch
        {
            "epoch-seconds" => ParseEpochSeconds(value),
            "http-date" => DateTimeOffset.ParseExact(
                value,
                "r",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal
            ),
            _ => DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
        };
    }

    private static DateTimeOffset ParseEpochSeconds(string value)
    {
        var epochSeconds = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        var wholeSeconds = decimal.Truncate(epochSeconds);
        var fractionalSeconds = epochSeconds - wholeSeconds;
        var ticks = (long)(fractionalSeconds * TimeSpan.TicksPerSecond);
        return new DateTimeOffset(
            DateTime.UnixEpoch.AddSeconds((double)wholeSeconds).AddTicks(ticks),
            TimeSpan.Zero
        );
    }

    private static string FormatEpochSeconds(DateTimeOffset value)
    {
        var seconds =
            (decimal)(value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks)
            / TimeSpan.TicksPerSecond;
        return seconds.ToString("0.0################", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
    }

    private static string FormatDateTimeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        if (utc.Ticks % TimeSpan.TicksPerSecond == 0)
        {
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        var text = utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        return text.TrimEnd('0') + "Z";
    }

    private static string? TryGetFirstHeaderValue(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        foreach (var header in headers)
        {
            if (
                string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
            )
                return header.Value[0];
        }

        return null;
    }

    private static string? NormalizeErrorType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        var colonIndex = normalized.IndexOf(':');
        if (colonIndex >= 0)
            normalized = normalized[..colonIndex];

        var hashIndex = normalized.LastIndexOf('#');
        if (hashIndex >= 0)
            normalized = normalized[(hashIndex + 1)..];

        return normalized;
    }
}
