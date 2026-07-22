using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using NSmithy.Http;
using NSmithy.Server;

namespace NSmithy.Server.AspNetCore;

/// <summary>
/// Binds ASP.NET Core to the shared <see cref="SmithyServerRuntime"/>. Generated endpoints call one
/// of the <c>Dispatch…</c> methods; this adapter owns the only two host-specific conversions —
/// building a neutral <see cref="SmithyHttpRequest"/> from an <see cref="HttpContext"/> and writing
/// a <see cref="SmithyServerResponse"/> back — and holds the framework dependency. It carries no
/// wire knowledge of any protocol: trailer values come from the response, and the adapter only
/// decides whether the connection can carry trailers.
/// </summary>
public static class SmithyAspNetCoreHost
{
    private const string BufferedRequestBodyItemKey =
        "NSmithy.Server.AspNetCore.BufferedRequestBody";

    // The runtime is currently stateless, so a shared default suffices and generated endpoints do
    // not depend on it being registered in DI. When server interceptors/telemetry land, this is
    // the seam that becomes configurable.
    private static readonly SmithyServerRuntime Runtime = new();

    /// <summary>Unary dispatch. Set <paramref name="streamRequestBody"/> for streaming blob input.</summary>
    public static async Task DispatchAsync<TInput, TOutput>(
        HttpContext httpContext,
        IServerOperationProtocol<TInput, TOutput> protocol,
        Func<TInput, CancellationToken, ValueTask<TOutput>> handler,
        bool streamRequestBody = false,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var request = await ToSmithyRequestAsync(httpContext, streamRequestBody, cancellationToken)
            .ConfigureAwait(false);
        var response = await Runtime
            .DispatchAsync(protocol, request, handler, cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(httpContext, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Output-stream dispatch: a unary request in, events out.</summary>
    public static async Task DispatchOutputStreamAsync<TInput, TOutputEvent>(
        HttpContext httpContext,
        IOutputEventStreamServerProtocol<TInput, TOutputEvent> protocol,
        Func<TInput, CancellationToken, IAsyncEnumerable<TOutputEvent>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var request = await ToSmithyRequestAsync(httpContext, streamBody: false, cancellationToken)
            .ConfigureAwait(false);
        var response = Runtime.DispatchOutputStream(protocol, request, handler, cancellationToken);
        await WriteAsync(httpContext, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Input-stream dispatch: events in, a unary response out.</summary>
    public static async Task DispatchInputStreamAsync<TInputEvent, TOutput>(
        HttpContext httpContext,
        IInputEventStreamServerProtocol<TInputEvent, TOutput> protocol,
        Func<IAsyncEnumerable<TInputEvent>, CancellationToken, ValueTask<TOutput>> handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        RelaxRequestBodyRate(httpContext);
        var request = await ToSmithyRequestAsync(httpContext, streamBody: true, cancellationToken)
            .ConfigureAwait(false);
        var response = await Runtime
            .DispatchInputStreamAsync(protocol, request, handler, cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(httpContext, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Duplex-stream dispatch: events in both directions.</summary>
    public static async Task DispatchDuplexStreamAsync<TInputEvent, TOutputEvent>(
        HttpContext httpContext,
        IDuplexEventStreamServerProtocol<TInputEvent, TOutputEvent> protocol,
        Func<
            IAsyncEnumerable<TInputEvent>,
            CancellationToken,
            IAsyncEnumerable<TOutputEvent>
        > handler,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        RelaxRequestBodyRate(httpContext);
        var request = await ToSmithyRequestAsync(httpContext, streamBody: true, cancellationToken)
            .ConfigureAwait(false);
        var response = Runtime.DispatchDuplexStream(protocol, request, handler, cancellationToken);
        await WriteAsync(httpContext, response, cancellationToken).ConfigureAwait(false);
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
            var content = await ReadRequestBodyContentAsync(httpContext, cancellationToken)
                .ConfigureAwait(false);
            request.Body =
                content.Length == 0 ? SmithyHttpBody.Empty : new SmithyHttpBody.Bytes(content);
        }

        return request;
    }

    private static async Task WriteAsync(
        HttpContext httpContext,
        SmithyServerResponse response,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(response);

        httpContext.Response.StatusCode = response.StatusCode;
        foreach (var header in response.Headers)
        {
            httpContext.Response.Headers[header.Key] = header.Value.ToArray();
        }

        if (response.ContentLength is { } contentLength)
        {
            httpContext.Response.ContentLength = contentLength;
        }

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
        if (feature is not null)
        {
            feature.MinDataRate = null;
        }
    }

    private static async Task<byte[]> ReadRequestBodyContentAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (httpContext.Items.TryGetValue(BufferedRequestBodyItemKey, out var cached))
        {
            return cached as byte[] ?? [];
        }

        using var stream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        var content = stream.ToArray();
        httpContext.Items[BufferedRequestBodyItemKey] = content;
        return content;
    }
}
