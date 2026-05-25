using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Protocols.RestJson;

namespace NSmithy.Server.AspNetCore;

public static class SmithyAspNetCoreProtocol
{
    private const string JsonRequestBodyItemKey = "NSmithy.Server.AspNetCore.JsonRequestBody";
    private static readonly SmithyJsonCodec JsonCodec = SmithyJsonCodec.Default;

    public static T GetRouteValue<T>(HttpContext httpContext, string name)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return httpContext.Request.RouteValues.TryGetValue(name, out var value) && value is not null
            ? RestJsonProtocol.ConvertHttpValue<T>(value.ToString())!
            : throw new InvalidOperationException($"Missing route value '{name}'.");
    }

    [return: MaybeNull]
    public static T GetQueryValue<T>(HttpContext httpContext, string name)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return httpContext.Request.Query.TryGetValue(name, out var values)
            ? RestJsonProtocol.ConvertHttpValue<T>(values.FirstOrDefault())
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

        return RestJsonProtocol.ConvertHttpValue<T>(values.FirstOrDefault())!;
    }

    [return: MaybeNull]
    public static T GetQueryParams<T>(HttpContext httpContext, IReadOnlyList<string> excludedNames)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(excludedNames);

        var excluded = new HashSet<string>(excludedNames, StringComparer.Ordinal);
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var query in httpContext.Request.Query)
        {
            if (excluded.Contains(query.Key))
            {
                continue;
            }

            values[query.Key] = query.Value.Select(value => value ?? string.Empty).ToArray();
        }

        return CreateQueryParamsMap<T>(values);
    }

    [return: MaybeNull]
    public static T GetHeaderValue<T>(HttpContext httpContext, string name)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return httpContext.Request.Headers.TryGetValue(name, out var values)
            ? RestJsonProtocol.ConvertHttpValue<T>(values.FirstOrDefault())
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

        return RestJsonProtocol.ConvertHttpValue<T>(values.FirstOrDefault())!;
    }

    [return: MaybeNull]
    public static T GetPrefixedHeaders<T>(HttpContext httpContext, string prefix)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(prefix);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in httpContext.Request.Headers)
        {
            if (!IsBindablePrefixedHeader(prefix, header.Key))
            {
                continue;
            }

            if (
                header.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
            )
            {
                values[header.Key[prefix.Length..]] = header.Value[0] ?? string.Empty;
            }
        }

        return RestJsonProtocol.CreateStringMap<T>(values);
    }

    public static async Task<T> ReadJsonRequestBodyAsync<T>(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
        where T : IDeserializableShape<T>
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        return content.Length == 0 ? default! : JsonCodec.Deserialize<T>(content);
    }

    public static async Task<T> ReadJsonRequestBodyAsync<T>(
        HttpContext httpContext,
        Func<IShapeDeserializer, T> read,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(read);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        return content.Length == 0 ? default! : JsonCodec.Deserialize(content, read);
    }

    public static async Task<T> ReadRequiredJsonRequestBodyAsync<T>(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
        where T : IDeserializableShape<T>
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

    public static async Task<T> ReadRequiredJsonRequestBodyAsync<T>(
        HttpContext httpContext,
        Func<IShapeDeserializer, T> read,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(read);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Missing JSON request body.");
        }

        return JsonCodec.Deserialize(content, read);
    }

    public static async Task WriteJsonResponseAsync<T>(
        HttpContext httpContext,
        T value,
        CancellationToken cancellationToken = default
    )
        where T : ISerializableShape
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.ContentType = "application/json";
        var content = JsonCodec.Serialize(value);
        await httpContext
            .Response.Body.WriteAsync(content, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task WriteJsonResponseAsync(
        HttpContext httpContext,
        Action<IShapeSerializer> write,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(write);

        httpContext.Response.ContentType = "application/json";
        var content = JsonCodec.Serialize(write);
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

        httpContext.Response.Headers[name] = RestJsonProtocol.FormatHttpValue(value);
    }

    public static void AddResponseHeader(
        HttpContext httpContext,
        string name,
        Schema schema,
        object? value
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (value is null)
        {
            return;
        }

        httpContext.Response.Headers[name] = RestJsonProtocol.FormatHttpValue(schema, value);
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

        foreach (var item in RestJsonProtocol.EnumerateStringMap(value))
        {
            if (item.Value is null)
            {
                continue;
            }

            var headerName = $"{prefix}{item.Key}";
            if (httpContext.Response.Headers.ContainsKey(headerName))
            {
                continue;
            }

            httpContext.Response.Headers[headerName] = RestJsonProtocol.FormatHttpValue(item.Value);
        }
    }

    public static void SetStatusCode(HttpContext httpContext, object value)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(value);

        httpContext.Response.StatusCode = Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public static bool HasExpectedQueryLiteral(
        HttpContext httpContext,
        string name,
        string? expectedValue
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!httpContext.Request.Query.TryGetValue(name, out var values))
        {
            return false;
        }

        if (expectedValue is null)
        {
            return true;
        }

        return values.Any(value => string.Equals(value, expectedValue, StringComparison.Ordinal));
    }

    public static async Task<byte[]> ReadPayloadBodyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<string?> ReadStringPayloadBodyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        return content.Length == 0 ? null : Encoding.UTF8.GetString(content);
    }

    // Reflection is used here to construct the map type at runtime. @httpQueryParams is not
    // on the hot path for most services, so the overhead is acceptable.
    private static T CreateQueryParamsMap<T>(Dictionary<string, IReadOnlyList<string>> values)
    {
        if (values.Count == 0)
        {
            return default!;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var singleValues = values.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Count > 0 ? entry.Value[0] ?? string.Empty : string.Empty,
            StringComparer.Ordinal
        );

        if (TryCreateMap(targetType, typeof(string), singleValues, out var stringMap))
        {
            return (T)stringMap!;
        }

        var mapCtor = targetType
            .GetConstructors()
            .FirstOrDefault(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType.IsGenericType
                    && (
                        parameters[0].ParameterType.GetGenericTypeDefinition()
                            == typeof(IReadOnlyDictionary<,>)
                        || parameters[0].ParameterType.GetGenericTypeDefinition()
                            == typeof(Dictionary<,>)
                    );
            });
        if (mapCtor is null)
        {
            throw new InvalidOperationException(
                $"Cannot create query params map type '{targetType}'."
            );
        }

        var valueType = mapCtor.GetParameters()[0].ParameterType.GetGenericArguments()[1];
        var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
        var dict = (System.Collections.IDictionary)Activator.CreateInstance(dictType)!;
        foreach (var entry in values)
        {
            dict[entry.Key] = CreateQueryParamValue(valueType, entry.Value);
        }

        return (T)mapCtor.Invoke([dict]);
    }

    private static bool TryCreateMap(
        Type targetType,
        Type valueType,
        IReadOnlyDictionary<string, string> values,
        out object? created
    )
    {
        if (targetType.IsAssignableFrom(typeof(Dictionary<string, string>)))
        {
            created = new Dictionary<string, string>(values, StringComparer.Ordinal);
            return true;
        }

        var readonlyCtor = targetType.GetConstructor([typeof(IReadOnlyDictionary<string, string>)]);
        if (readonlyCtor is not null)
        {
            created = readonlyCtor.Invoke([
                new Dictionary<string, string>(values, StringComparer.Ordinal),
            ]);
            return true;
        }

        var dictionaryCtor = targetType.GetConstructor([typeof(Dictionary<string, string>)]);
        if (dictionaryCtor is not null)
        {
            created = dictionaryCtor.Invoke([
                new Dictionary<string, string>(values, StringComparer.Ordinal),
            ]);
            return true;
        }

        created = null;
        return false;
    }

    private static object CreateQueryParamValue(Type valueType, IReadOnlyList<string> values)
    {
        if (valueType == typeof(string))
        {
            return values.Count > 0 ? values[0] ?? string.Empty : string.Empty;
        }

        if (values.Count == 0)
        {
            return string.Empty;
        }

        var enumerableCtor = valueType.GetConstructor([typeof(IEnumerable<string>)]);
        if (enumerableCtor is not null)
        {
            return enumerableCtor.Invoke([values]);
        }

        var readonlyCtor = valueType.GetConstructor([typeof(IReadOnlyList<string>)]);
        if (readonlyCtor is not null)
        {
            return readonlyCtor.Invoke([values]);
        }

        var arrayCtor = valueType.GetConstructor([typeof(string[])]);
        if (arrayCtor is not null)
        {
            return arrayCtor.Invoke([values.ToArray()]);
        }

        throw new InvalidOperationException(
            $"Cannot create query params value type '{valueType}' from repeated values."
        );
    }

    private static bool IsBindablePrefixedHeader(string prefix, string headerName)
    {
        if (!headerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !headerName.Equals("Host", StringComparison.OrdinalIgnoreCase)
            && !headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
            && !headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
            && !headerName.Equals("Accept", StringComparison.OrdinalIgnoreCase)
            && !headerName.Equals("User-Agent", StringComparison.OrdinalIgnoreCase);
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
