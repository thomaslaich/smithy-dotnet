using System.Net;
using System.Net.Http.Headers;
using NSmithy.Http;

namespace NSmithy.Protocols.Grpc;

public sealed class GrpcEventStreamHttpClientTransport : IEventStreamHttpTransport
{
    private readonly HttpClient httpClient;
    private readonly Uri? endpoint;

    public GrpcEventStreamHttpClientTransport(HttpClient httpClient, Uri? endpoint = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (endpoint is not null && !endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));
        }

        this.endpoint = endpoint;
    }

    public async Task<SmithyEventStreamHttpResponse> SendAsync(
        SmithyEventStreamHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = new HttpRequestMessage(request.Method, ResolveRequestUri(request.RequestUri))
        {
            Version = httpClient.DefaultRequestVersion,
            VersionPolicy = httpClient.DefaultVersionPolicy,
            Content = new GrpcEventStreamContent(request.Events, request.ContentType),
        };
        foreach (var header in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in request.ContentHeaders)
        {
            message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var headers = ToHeaderDictionary(response.Headers);
        var contentHeaders = response.Content is null
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            : ToHeaderDictionary(response.Content.Headers);

        if (!IsSuccessfulGrpcResponse(response))
        {
            // Transport-level failure (a 404, a 500 page, an HTTP/1.1 downgrade, …). Dispose eagerly
            // so the connection is returned to the pool even when the caller inspects only
            // StatusCode/Headers and never enumerates Events; the informative error is then raised by
            // the protocol's EnsureGrpcResponse from these captured status/headers.
            var statusCode = response.StatusCode;
            var reasonPhrase = response.ReasonPhrase;
            response.Dispose();
            return new SmithyEventStreamHttpResponse(
                statusCode,
                reasonPhrase,
                EmptyEvents(),
                headers,
                contentHeaders
            );
        }

        var responseStream = response.Content is null
            ? Stream.Null
            : await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return new SmithyEventStreamHttpResponse(
            response.StatusCode,
            response.ReasonPhrase,
            ReadResponseEvents(response, responseStream, cancellationToken),
            headers,
            contentHeaders
        );
    }

    private static bool IsSuccessfulGrpcResponse(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        var mediaType = response.Content?.Headers.ContentType?.MediaType;
        return mediaType is not null
            && mediaType.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase);
    }

    private static async IAsyncEnumerable<SmithyEventFrame> EmptyEvents()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private string ResolveRequestUri(string requestUri)
    {
        if (endpoint is null || IsHttpAbsoluteUri(requestUri))
        {
            return requestUri;
        }

        var endpointText = endpoint.ToString().TrimEnd('/');
        var requestText = requestUri.TrimStart('/');
        return $"{endpointText}/{requestText}";
    }

    private static bool IsHttpAbsoluteUri(string requestUri)
    {
        return Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)
            && uri.IsAbsoluteUri
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static async IAsyncEnumerable<SmithyEventFrame> ReadResponseEvents(
        HttpResponseMessage response,
        Stream responseStream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        using (response)
        using (responseStream)
        {
            await foreach (
                var frame in GrpcMessageFraming
                    .ReadAllAsync(responseStream, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                yield return frame;
            }

            // The stream is now fully read, so the gRPC trailers have arrived. A non-zero (or
            // missing) grpc-status means the server-side call failed: surface it as an exception
            // instead of letting the caller treat the partial events as a successful result.
            ThrowIfGrpcError(response);
        }
    }

    private static void ThrowIfGrpcError(HttpResponseMessage response)
    {
        var status = GetTrailerOrHeader(response, "grpc-status");
        if (status is null)
        {
            throw new InvalidOperationException(
                "gRPC stream ended without a grpc-status trailer; treating it as an incomplete, failed response."
            );
        }

        if (string.Equals(status, "0", StringComparison.Ordinal))
        {
            return;
        }

        var message = GetTrailerOrHeader(response, "grpc-message");
        var name =
            int.TryParse(
                status,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var code
            ) && Enum.IsDefined(typeof(GrpcStatus), code)
                ? ((GrpcStatus)code).ToString()
                : "Unknown";
        throw new InvalidOperationException(
            $"gRPC stream failed with status {status} ({name})"
                + (string.IsNullOrEmpty(message) ? "." : $": {message}")
        );
    }

    private static string? GetTrailerOrHeader(HttpResponseMessage response, string name)
    {
        if (response.TrailingHeaders.TryGetValues(name, out var trailers))
        {
            return trailers.FirstOrDefault();
        }

        return response.Headers.TryGetValues(name, out var headers)
            ? headers.FirstOrDefault()
            : null;
    }

    private static Dictionary<string, IReadOnlyList<string>> ToHeaderDictionary(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers
    )
    {
        return headers.ToDictionary(
            header => header.Key,
            header => (IReadOnlyList<string>)header.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase
        );
    }

    private sealed class GrpcEventStreamContent : HttpContent
    {
        private readonly IAsyncEnumerable<SmithyEventFrame> events;

        public GrpcEventStreamContent(
            IAsyncEnumerable<SmithyEventFrame> events,
            string? contentType
        )
        {
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context
        )
        {
            await foreach (var frame in events.ConfigureAwait(false))
            {
                await GrpcMessageFraming.WriteAsync(stream, frame.Payload).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken
        )
        {
            await foreach (
                var frame in events.WithCancellation(cancellationToken).ConfigureAwait(false)
            )
            {
                await GrpcMessageFraming
                    .WriteAsync(stream, frame.Payload, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
