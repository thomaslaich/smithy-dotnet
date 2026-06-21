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

    // gRPC always returns HTTP 200 and conveys status in trailers. These header names produced by
    // GrpcProtocol must be emitted as HTTP/2 trailers (after the body), not as leading headers.
    private static readonly string[] GrpcTrailerNames =
    [
        "grpc-status",
        "grpc-message",
        "x-smithy-grpc-error",
    ];

    /// <summary>
    /// Writes a gRPC response: the framed body as a leading <c>application/grpc</c> message followed
    /// by <c>grpc-status</c>/<c>grpc-message</c> as HTTP/2 trailers. Falls back to leading headers
    /// when the connection does not support trailers (e.g. HTTP/1.1 in tests), which keeps
    /// NSmithy↔NSmithy interop working off the same header dictionary.
    /// </summary>
    public static async Task WriteSmithyGrpcResponseAsync(
        HttpContext httpContext,
        SmithyHttpResponse response,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(response);

        httpContext.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.ContentHeaders)
        {
            httpContext.Response.Headers[header.Key] = header.Value.ToArray();
        }

        var supportsTrailers = httpContext.Response.SupportsTrailers();
        var trailers = new List<KeyValuePair<string, IReadOnlyList<string>>>();
        foreach (var header in response.Headers)
        {
            if (
                supportsTrailers
                && GrpcTrailerNames.Contains(header.Key, StringComparer.OrdinalIgnoreCase)
            )
            {
                trailers.Add(header);
            }
            else
            {
                httpContext.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        if (trailers.Count > 0)
        {
            httpContext.Response.DeclareTrailer(string.Join(", ", trailers.Select(t => t.Key)));
        }

        if (response.Content.Length > 0)
        {
            await httpContext
                .Response.Body.WriteAsync(response.Content, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var trailer in trailers)
        {
            foreach (var value in trailer.Value)
            {
                httpContext.Response.AppendTrailer(trailer.Key, value);
            }
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
