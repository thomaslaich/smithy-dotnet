using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using NSmithy.Codecs.Json;
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

        return RestJsonProtocol.CreateStringMap<T>(values);
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

            httpContext.Response.Headers[$"{prefix}{item.Key}"] = RestJsonProtocol.FormatHttpValue(
                item.Value
            );
        }
    }

    public static void SetStatusCode(HttpContext httpContext, object value)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(value);

        httpContext.Response.StatusCode = Convert.ToInt32(value, CultureInfo.InvariantCulture);
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
