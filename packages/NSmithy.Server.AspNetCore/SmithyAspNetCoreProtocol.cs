using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NSmithy.Http;

namespace NSmithy.Server.AspNetCore;

public static class SmithyAspNetCoreProtocol
{
    private const string JsonRequestBodyItemKey = "NSmithy.Server.AspNetCore.JsonRequestBody";

    public static async Task<SmithyHttpRequest> CreateSmithyHttpRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Prefer the raw (still percent-encoded) request target so HTTP-label binding sees the wire
        // form — e.g. a greedy `{label+}` carrying an escaped `%2F` must not be pre-decoded into
        // extra path segments. Fall back to the (decoded) PathBase/Path when RawTarget is absent.
        var rawTarget = httpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        var requestTarget = string.IsNullOrEmpty(rawTarget)
            ? httpContext.Request.PathBase.ToString()
                + httpContext.Request.Path.ToString()
                + httpContext.Request.QueryString.ToString()
            : rawTarget;
        var request = new SmithyHttpRequest(
            new HttpMethod(httpContext.Request.Method),
            requestTarget
        );
        foreach (var header in httpContext.Request.Headers)
        {
            request.Headers[header.Key] = [.. header.Value.Select(value => value ?? string.Empty)];
        }

        request.ContentType = httpContext.Request.ContentType;
        request.Content = await ReadRequestBodyContentAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        return request;
    }

    public static async Task WriteSmithyHttpResponseAsync(
        HttpContext httpContext,
        SmithyHttpResponse response,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(response);

        httpContext.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            httpContext.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.ContentHeaders)
        {
            httpContext.Response.Headers[header.Key] = header.Value.ToArray();
        }

        if (response.Content.Length > 0)
        {
            await httpContext
                .Response.Body.WriteAsync(response.Content, cancellationToken)
                .ConfigureAwait(false);
        }
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

    private static async Task<byte[]> ReadRequestBodyContentAsync(
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
