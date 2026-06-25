using System.Buffers.Binary;
using System.Runtime.CompilerServices;
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

    public static SmithyEventStreamHttpRequest CreateSmithyGrpcEventStreamRequest(
        HttpContext httpContext,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var request = new SmithyEventStreamHttpRequest(
            new HttpMethod(httpContext.Request.Method),
            httpContext.Request.PathBase.ToString() + httpContext.Request.Path.ToString()
        )
        {
            ContentType = httpContext.Request.ContentType,
            Events = ReadGrpcFramesAsync(httpContext.Request.Body, cancellationToken),
        };
        foreach (var header in httpContext.Request.Headers)
        {
            request.Headers[header.Key] = [.. header.Value.Select(value => value ?? string.Empty)];
        }

        return request;
    }

    public static async Task WriteSmithyGrpcEventStreamResponseAsync(
        HttpContext httpContext,
        IAsyncEnumerable<SmithyEventFrame> events,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(events);

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers.ContentType = "application/grpc+proto";

        var supportsTrailers = httpContext.Response.SupportsTrailers();
        if (supportsTrailers)
        {
            httpContext.Response.DeclareTrailer("grpc-status");
        }

        await foreach (
            var frame in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            await WriteGrpcFrameAsync(httpContext.Response.Body, frame.Payload, cancellationToken)
                .ConfigureAwait(false);
            await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (supportsTrailers)
        {
            httpContext.Response.AppendTrailer("grpc-status", "0");
        }
        else
        {
            httpContext.Response.Headers["grpc-status"] = "0";
        }
    }

    public static Task WriteSmithyGrpcEventStreamResponseAsync(
        HttpContext httpContext,
        SmithyEventFrame eventFrame,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventFrame);
        return WriteSmithyGrpcEventStreamResponseAsync(
            httpContext,
            SingleEvent(eventFrame),
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

    private static async IAsyncEnumerable<SmithyEventFrame> ReadGrpcFramesAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var header = new byte[5];
        while (await TryReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            if (header[0] != 0)
            {
                throw new NotSupportedException(
                    "Compressed gRPC messages are not yet supported by NSmithy."
                );
            }

            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1, 4));
            var payload = new byte[length];
            if (
                length > 0
                && !await TryReadExactAsync(stream, payload, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                throw new InvalidOperationException("Truncated gRPC frame payload.");
            }

            yield return new SmithyEventFrame(payload);
        }
    }

    private static async ValueTask WriteGrpcFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken
    )
    {
        var header = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1, 4), (uint)payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<bool> TryReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken
    )
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream
                .ReadAsync(buffer[read..], cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                if (read == 0)
                {
                    return false;
                }

                throw new InvalidOperationException("Truncated gRPC frame header.");
            }

            read += count;
        }

        return true;
    }

    private static async IAsyncEnumerable<SmithyEventFrame> SingleEvent(SmithyEventFrame eventFrame)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return eventFrame;
    }
}
