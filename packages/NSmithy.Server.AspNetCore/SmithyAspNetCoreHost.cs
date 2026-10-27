using System.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSmithy.Http;
using NSmithy.Server;

namespace NSmithy.Server.AspNetCore;

/// <summary>
/// Binds ASP.NET Core to the shared <see cref="SmithyServerRuntime"/>. Generated endpoints call one
/// of the <c>Dispatch…</c> methods; this adapter owns the only two host-specific conversions —
/// building a neutral <see cref="SmithyHttpRequest"/> from an <see cref="HttpContext"/> and writing
/// a <see cref="SmithyHttpServerResponse"/> back — and holds the framework dependency. It carries no
/// wire knowledge of any protocol: trailer values come from the response, and the adapter only
/// decides whether the connection can carry trailers.
/// </summary>
public static class SmithyAspNetCoreHost
{
    private const string BufferedRequestBodyItemKey =
        "NSmithy.Server.AspNetCore.BufferedRequestBody";

    /// <summary>Dispatch. Set <paramref name="streamRequestBody"/> for streaming request bodies.</summary>
    public static async Task DispatchAsync<TInput, TOutput>(
        SmithyServerRuntime runtime,
        HttpContext httpContext,
        IServerOperationProtocol<TInput, TOutput> protocol,
        Func<TInput, CancellationToken, Task<TOutput>> handler,
        bool streamRequestBody = false,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(httpContext);
        if (streamRequestBody)
        {
            RelaxRequestBodyRate(httpContext);
        }

        var request = await ToSmithyRequestAsync(httpContext, streamRequestBody, cancellationToken)
            .ConfigureAwait(false);
        var response = await runtime
            .DispatchAsync(protocol, request, handler, cancellationToken)
            .ConfigureAwait(false);
        using (response)
        {
            await WriteAsync(httpContext, response, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dispatches a request to an operation bound to a protocol. The runtime is the one the
    /// request's services provide, or a default runtime when none is registered; the handler comes
    /// from the request's services.
    /// </summary>
    public static async Task DispatchAsync(
        HttpContext httpContext,
        IServerOperationBinding operation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.StreamsRequestBody)
        {
            RelaxRequestBodyRate(httpContext);
        }

        var request = await ToSmithyRequestAsync(
                httpContext,
                operation.StreamsRequestBody,
                cancellationToken
            )
            .ConfigureAwait(false);
        await DispatchBuiltRequestAsync(httpContext, operation, request, cancellationToken)
            .ConfigureAwait(false);
    }

    // For callers that already built the request, such as dispatch routing, which reads the
    // request to resolve the operation before it can dispatch.
    internal static async Task DispatchBuiltRequestAsync(
        HttpContext httpContext,
        IServerOperationBinding operation,
        SmithyHttpRequest request,
        CancellationToken cancellationToken
    )
    {
        var services = httpContext.RequestServices;
        var response = await operation
            .DispatchAsync(
                services.GetService<SmithyServerRuntime>() ?? SmithyServerRuntime.Default,
                request,
                services,
                cancellationToken
            )
            .ConfigureAwait(false);
        using (response)
        {
            await WriteAsync(httpContext, response, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static Task WriteResponseAsync(
        HttpContext httpContext,
        SmithyHttpServerResponse response,
        CancellationToken cancellationToken
    ) => WriteAsync(httpContext, response, cancellationToken);

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

    // ---------------- host <-> neutral conversion ----------------

    /// <summary>
    /// Builds a neutral <see cref="SmithyHttpRequest"/> from the host request. Public so custom
    /// endpoints can reuse the adapter's conversion; the generated endpoints call it internally via
    /// the <c>Dispatch…</c> methods. Set <paramref name="streamBody"/> to expose the request body as
    /// a live stream (streaming blob or event-stream input) instead of buffering it.
    /// </summary>
    public static async Task<SmithyHttpRequest> ToSmithyRequestAsync(
        HttpContext httpContext,
        bool streamBody = false,
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
            HttpMethod.Parse(httpContext.Request.Method),
            requestTarget
        );
        foreach (var (name, values) in httpContext.Request.Headers)
        {
            request.Headers[name] =
                values.Count == 1 ? [values[0] ?? string.Empty] : ToList(values);
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
            var content = await ReadRequestBodyContentAsync(httpContext, cancellationToken)
                .ConfigureAwait(false);
            request.Body =
                content.Length == 0 ? SmithyHttpBody.Empty : new SmithyHttpBody.Bytes(content);
        }

        return request;
    }

    private static string[] ToList(StringValues values)
    {
        var list = new string[values.Count];
        for (var index = 0; index < list.Length; index++)
        {
            list[index] = values[index] ?? string.Empty;
        }

        return list;
    }

    private static StringValues ToStringValues(IReadOnlyList<string> values) =>
        values switch
        {
            [var value] => new StringValues(value),
            string[] array => new StringValues(array),
            _ => new StringValues([.. values]),
        };

    private static Task WriteAsync(
        HttpContext httpContext,
        SmithyHttpServerResponse response,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(response);

        httpContext.Response.StatusCode = response.StatusCode;
        // Through the concrete type when it is one: the interface would box its enumerator.
        if (response.Headers is Dictionary<string, IReadOnlyList<string>> headers)
        {
            foreach (var (name, values) in headers)
            {
                httpContext.Response.Headers[name] = ToStringValues(values);
            }
        }
        else
        {
            foreach (var (name, values) in response.Headers)
            {
                httpContext.Response.Headers[name] = ToStringValues(values);
            }
        }

        if (response.ContentLength is { } contentLength)
        {
            httpContext.Response.ContentLength = contentLength;
        }

        // A unary response is one buffered chunk with nothing to follow it: written in one call,
        // and the framework starts and completes the response around it.
        if (response.Trailers is null && response.TryGetSingleChunk(out var content))
        {
            return content.IsEmpty
                ? Task.CompletedTask
                : WriteContentAsync(httpContext, content, cancellationToken);
        }

        return WriteChunksAsync(httpContext, response, cancellationToken);
    }

    private static async Task WriteContentAsync(
        HttpContext httpContext,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await httpContext
                .Response.BodyWriter.WriteAsync(content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static async Task WriteChunksAsync(
        HttpContext httpContext,
        SmithyHttpServerResponse response,
        CancellationToken cancellationToken
    )
    {
        var supportsTrailers =
            response.Trailers is not null && httpContext.Response.SupportsTrailers();
        if (response.Trailers is { } upfront && !supportsTrailers)
        {
            // The connection cannot carry trailers (e.g. HTTP/1.1 in tests). Fold the clean-completion
            // trailer set into leading headers so header-dictionary peers still observe them; these
            // must be set before the body is flushed, so a mid-stream failure instead aborts the body.
            foreach (var trailer in upfront(null))
            {
                httpContext.Response.Headers[trailer.Key] = new[] { trailer.Value };
            }
        }

        await httpContext.Response.StartAsync(cancellationToken).ConfigureAwait(false);

        Exception? streamError = null;
        try
        {
            await foreach (
                var chunk in response.Body.WithCancellation(cancellationToken).ConfigureAwait(false)
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
        catch (Exception ex) when (supportsTrailers)
        {
            // Reflected into the trailers below; without trailer support the exception propagates
            // and aborts the response so the peer sees a broken stream, not a clean completion.
            streamError = ex;
        }

        if (supportsTrailers)
        {
            foreach (var trailer in response.Trailers!(streamError))
            {
                httpContext.Response.AppendTrailer(trailer.Key, trailer.Value);
            }
        }

        await httpContext.Response.CompleteAsync().ConfigureAwait(false);
    }

    private static void RelaxRequestBodyRate(HttpContext httpContext)
    {
        var feature = httpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>();
        feature?.MinDataRate = null;
    }

    private static async ValueTask<byte[]> ReadRequestBodyContentAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (
            httpContext.Request.ContentLength == 0
            || (
                httpContext.Request.ContentLength is null
                && httpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
                    == false
            )
        )
        {
            return [];
        }

        if (httpContext.Items.TryGetValue(BufferedRequestBodyItemKey, out var cached))
        {
            return cached as byte[] ?? [];
        }

        var content = await ReadToEndAsync(
                httpContext.Request.Body,
                httpContext.Request.ContentLength,
                cancellationToken
            )
            .ConfigureAwait(false);
        httpContext.Items[BufferedRequestBodyItemKey] = content;
        return content;
    }

    // Reads into a pooled buffer that grows only as bytes arrive, then copies them out once. A
    // declared Content-Length only sizes the first buffer, up to a cap: it is the client's claim,
    // and must not make the server allocate before the bytes exist.
    private static async Task<byte[]> ReadToEndAsync(
        Stream body,
        long? declaredLength,
        CancellationToken cancellationToken
    )
    {
        const int FirstBufferCap = 64 * 1024;
        var buffer = ArrayPool<byte>.Shared.Rent(
            declaredLength is { } length ? (int)Math.Min(length + 1, FirstBufferCap) : 4096
        );
        var count = 0;
        try
        {
            while (true)
            {
                if (count == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, count).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var read = await body.ReadAsync(buffer.AsMemory(count), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.AsSpan(0, count).ToArray();
                }

                count += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
