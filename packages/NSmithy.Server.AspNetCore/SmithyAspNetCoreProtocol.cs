using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using NSmithy.Http;
using NSmithy.Protocols.Grpc;

namespace NSmithy.Server.AspNetCore;

public static class SmithyAspNetCoreProtocol
{
    private const string JsonRequestBodyItemKey = "NSmithy.Server.AspNetCore.JsonRequestBody";

    public static async Task<SmithyHttpRequest> CreateSmithyHttpRequestAsync(
        HttpContext httpContext,
        bool streamBody,
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
        if (streamBody)
        {
            request.Body = new SmithyHttpBody.Streaming(
                httpContext.Request.Body,
                httpContext.Request.ContentLength
            );
        }
        else
        {
            request.Body = ToHttpBody(
                await ReadRequestBodyContentAsync(httpContext, cancellationToken)
                    .ConfigureAwait(false)
            );
        }
        return request;
    }

    public static Task<SmithyHttpRequest> CreateSmithyHttpRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    ) => CreateSmithyHttpRequestAsync(httpContext, streamBody: false, cancellationToken);

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

        if (response.Body is SmithyHttpBody.Streaming streamBody)
        {
            if (streamBody.ContentLength is { } contentLength)
            {
                httpContext.Response.ContentLength = contentLength;
            }

            await streamBody
                .Content.CopyToAsync(httpContext.Response.Body, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (response.Body is SmithyHttpBody.Bytes bytesBody && bytesBody.Content.Length > 0)
        {
            await httpContext
                .Response.Body.WriteAsync(bytesBody.Content, cancellationToken)
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

    /// <summary>
    /// Prepares the request for event-stream reads and returns its raw body stream. The bound
    /// server protocol deframes and decodes the events itself.
    /// </summary>
    public static Stream GetEventStreamRequestBody(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var minRequestBodyDataRateFeature =
            httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>();
        if (minRequestBodyDataRateFeature is not null)
        {
            minRequestBodyDataRateFeature.MinDataRate = null;
        }

        return httpContext.Request.Body;
    }

    public static async Task WriteSmithyGrpcEventStreamResponseAsync(
        HttpContext httpContext,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(chunks);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers.ContentType = "application/grpc+proto";

        var supportsTrailers = httpContext.Response.SupportsTrailers();
        if (supportsTrailers)
        {
            httpContext.Response.DeclareTrailer("grpc-status");
            httpContext.Response.DeclareTrailer("grpc-message");
        }
        else
        {
            // No HTTP/2 trailers (e.g. HTTP/1.1 in tests): grpc-status can only travel as a leading
            // header, so it must be set before the body is flushed — we cannot know the final status
            // yet, so we optimistically signal success. A mid-stream failure is instead surfaced by
            // letting the exception abort the response (below) so the client sees a broken stream
            // rather than trusting this header. Setting it after StartAsync would throw.
            httpContext.Response.Headers["grpc-status"] = "0";
        }

        await httpContext.Response.StartAsync(cancellationToken).ConfigureAwait(false);

        var status = GrpcStatus.Ok;
        string? message = null;
        try
        {
            await foreach (
                var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false)
            )
            {
                await httpContext
                    .Response.Body.WriteAsync(chunk, cancellationToken)
                    .ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!supportsTrailers)
            {
                // The leading grpc-status:0 has already been flushed and cannot be changed. Surface
                // the failure as an aborted stream so the client does not read it as a clean,
                // successful completion.
                throw;
            }

            status = GrpcStatus.Internal;
            message = ex.Message;
        }

        if (supportsTrailers)
        {
            httpContext.Response.AppendTrailer(
                "grpc-status",
                ((int)status).ToString(CultureInfo.InvariantCulture)
            );
            if (message is not null)
            {
                httpContext.Response.AppendTrailer("grpc-message", message);
            }
        }

        await httpContext.Response.CompleteAsync().ConfigureAwait(false);
    }

    public static Task WriteSmithyGrpcEventStreamResponseAsync(
        HttpContext httpContext,
        ReadOnlyMemory<byte> framedMessage,
        CancellationToken cancellationToken = default
    )
    {
        return WriteSmithyGrpcEventStreamResponseAsync(
            httpContext,
            SingleChunk(framedMessage),
            cancellationToken
        );
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

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> SingleChunk(
        ReadOnlyMemory<byte> framedMessage
    )
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return framedMessage;
    }

    private static SmithyHttpBody ToHttpBody(byte[] content) =>
        content.Length == 0 ? SmithyHttpBody.Empty : new SmithyHttpBody.Bytes(content);
}
