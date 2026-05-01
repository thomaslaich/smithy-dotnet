using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using SmithyNet.Codecs.Json;

namespace SmithyNet.Server.AspNetCore;

public static class SmithyAspNetCoreProtocol
{
    private const string JsonRequestBodyItemKey = "SmithyNet.Server.AspNetCore.JsonRequestBody";
    private static readonly SmithyJsonPayloadCodec JsonCodec = SmithyJsonPayloadCodec.Default;

    public static T GetRouteValue<T>(HttpContext httpContext, string name)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return httpContext.Request.RouteValues.TryGetValue(name, out var value) && value is not null
            ? ConvertHttpValue<T>(value.ToString())!
            : throw new InvalidOperationException($"Missing route value '{name}'.");
    }

    [return: MaybeNull]
    public static T GetQueryValue<T>(HttpContext httpContext, string name)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return httpContext.Request.Query.TryGetValue(name, out var values)
            ? ConvertHttpValue<T>(values.FirstOrDefault())
            : default;
    }

    public static T GetRequiredQueryValue<T>(HttpContext httpContext, string name)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!httpContext.Request.Query.TryGetValue(name, out var values))
        {
            throw new InvalidOperationException($"Missing query value '{name}'.");
        }

        return ConvertHttpValue<T>(values.FirstOrDefault())!;
    }

    [return: MaybeNull]
    public static T GetQueryParams<T>(HttpContext httpContext, IReadOnlyList<string> excludedNames)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(excludedNames);

        var excluded = new HashSet<string>(excludedNames, StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var query in httpContext.Request.Query)
        {
            if (excluded.Contains(query.Key))
            {
                continue;
            }

            if (query.Value.Count > 0)
            {
                values[query.Key] = query.Value[0] ?? string.Empty;
            }
        }

        return CreateStringMap<T>(values);
    }

    [return: MaybeNull]
    public static T GetHeaderValue<T>(HttpContext httpContext, string name)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return httpContext.Request.Headers.TryGetValue(name, out var values)
            ? ConvertHttpValue<T>(values.FirstOrDefault())
            : default;
    }

    public static T GetRequiredHeaderValue<T>(HttpContext httpContext, string name)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!httpContext.Request.Headers.TryGetValue(name, out var values))
        {
            throw new InvalidOperationException($"Missing header value '{name}'.");
        }

        return ConvertHttpValue<T>(values.FirstOrDefault())!;
    }

    [return: MaybeNull]
    public static T GetPrefixedHeaders<T>(HttpContext httpContext, string prefix)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(prefix);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in httpContext.Request.Headers)
        {
            if (
                header.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
            )
            {
                values[header.Key[prefix.Length..]] = header.Value[0] ?? string.Empty;
            }
        }

        return CreateStringMap<T>(values);
    }

    public static async Task<T> ReadJsonRequestBodyAsync<T>(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        return content.Length == 0 ? default! : JsonCodec.Deserialize<T>(content);
    }

    public static async Task<T> ReadRequiredJsonRequestBodyAsync<T>(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Missing JSON request body.");
        }

        return JsonCodec.Deserialize<T>(content);
    }

    public static async Task WriteJsonResponseAsync<T>(
        HttpContext httpContext,
        T value,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.ContentType = "application/json";
        var content = JsonCodec.Serialize(value);
        await httpContext
            .Response.Body.WriteAsync(content, cancellationToken)
            .ConfigureAwait(false);
    }

    public static void AddResponseHeader(HttpContext httpContext, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (value is null)
        {
            return;
        }

        httpContext.Response.Headers[name] = FormatHttpValue(value);
    }

    public static void AddPrefixedResponseHeaders(
        HttpContext httpContext,
        string prefix,
        object? value
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(prefix);

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

            httpContext.Response.Headers[$"{prefix}{item.Key}"] = FormatHttpValue(item.Value);
        }
    }

    public static void SetStatusCode(HttpContext httpContext, object value)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(value);

        httpContext.Response.StatusCode = Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public static string FormatHttpValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            DateTimeOffset timestamp => timestamp
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
                ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
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
            return (T)Enum.Parse(targetType, value, ignoreCase: false);
        }

        var constructor = targetType.GetConstructor([typeof(string)]);
        if (constructor is not null)
        {
            return (T)constructor.Invoke([value]);
        }

        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    [return: MaybeNull]
    private static T CreateStringMap<T>(Dictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            return default;
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

    private static async Task<byte[]> ReadJsonRequestBodyContentAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (httpContext.Items.TryGetValue(JsonRequestBodyItemKey, out var cached))
        {
            return cached as byte[] ?? [];
        }

        using var stream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        var content = stream.ToArray();
        httpContext.Items[JsonRequestBodyItemKey] = content;
        return content;
    }
}
