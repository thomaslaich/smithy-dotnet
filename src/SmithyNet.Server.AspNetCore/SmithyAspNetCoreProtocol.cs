using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using SmithyNet.Json;

namespace SmithyNet.Server.AspNetCore;

public static class SmithyAspNetCoreProtocol
{
    private const string JsonRequestBodyItemKey = "SmithyNet.Server.AspNetCore.JsonRequestBody";

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

    public static async Task<T> ReadJsonRequestBodyAsync<T>(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        return SmithyJsonSerializer.Deserialize<T>(content);
    }

    [return: MaybeNull]
    public static async Task<T> ReadJsonRequestBodyMemberAsync<T>(
        HttpContext httpContext,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        using var document = System.Text.Json.JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty(name, out var value)
            ? SmithyJsonSerializer.Deserialize<T>(value.GetRawText())
            : default!;
    }

    public static async Task<T> ReadRequiredJsonRequestBodyMemberAsync<T>(
        HttpContext httpContext,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var content = await ReadJsonRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"Missing JSON request body member '{name}'."
            );
        }

        using var document = System.Text.Json.JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty(name, out var value)
            ? SmithyJsonSerializer.Deserialize<T>(value.GetRawText())
            : throw new InvalidOperationException($"Missing JSON request body member '{name}'.");
    }

    public static async Task WriteJsonResponseAsync<T>(
        HttpContext httpContext,
        T value,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.ContentType = "application/json";
        await httpContext
            .Response.WriteAsync(SmithyJsonSerializer.Serialize(value), cancellationToken)
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
            return default!;
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

    private static async Task<string> ReadJsonRequestBodyContentAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (httpContext.Items.TryGetValue(JsonRequestBodyItemKey, out var cached))
        {
            return cached as string ?? string.Empty;
        }

        using var reader = new StreamReader(httpContext.Request.Body);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        httpContext.Items[JsonRequestBodyItemKey] = content;
        return content;
    }
}
