using System.Net;
using System.Net.Http.Headers;

namespace NSmithy.Http;

public sealed class HttpClientTransport : IHttpTransport
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoHeaders =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        );

    private readonly HttpClient httpClient;

    public HttpClientTransport(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public Task<SmithyHttpClientResponse> SendAsync(
        SmithyHttpRequest request,
        SmithyHttpClientResponseMode responseMode,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendCoreAsync(request, responseMode, cancellationToken);
    }

    private async Task<SmithyHttpClientResponse> SendCoreAsync(
        SmithyHttpRequest request,
        SmithyHttpClientResponseMode responseMode,
        CancellationToken cancellationToken
    )
    {
        using var message = CreateMessage(request);
        var response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (responseMode == SmithyHttpClientResponseMode.Stream)
        {
            var contentHeaders = response.Content is null
                ? NoHeaders
                : ToHeaderDictionary(response.Content.Headers);
            return new SmithyHttpClientResponse(
                response.StatusCode,
                response.ReasonPhrase,
                new SmithyHttpBody.Streaming(
                    new ResponseOwningStream(
                        response,
                        response.Content is null
                            ? Stream.Null
                            : await response
                                .Content.ReadAsStreamAsync(cancellationToken)
                                .ConfigureAwait(false)
                    ),
                    response.Content?.Headers.ContentLength
                ),
                ToHeaderDictionary(response.Headers),
                contentHeaders,
                name => GetTrailer(response, name)
            );
        }

        using var bufferedResponse = response;
        var content = response.Content is null
            ? []
            : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // The body is fully read, so any HTTP/2 trailers have arrived. Capture the leading and
        // trailing headers now: the returned Trailer closure must not read them off `response`,
        // which is disposed the moment this method returns.
        var headers = ToHeaderDictionary(response.Headers);
        var trailers = ToHeaderDictionary(response.TrailingHeaders);
        Func<string, string?>? getTrailer =
            trailers.Count == 0 && headers.Count == 0
                ? null
                : name => GetCapturedTrailer(trailers, headers, name);

        return new SmithyHttpClientResponse(
            response.StatusCode,
            response.ReasonPhrase,
            content.Length == 0 ? SmithyHttpBody.Empty : new SmithyHttpBody.Bytes(content),
            headers,
            response.Content is null ? NoHeaders : ToHeaderDictionary(response.Content.Headers),
            getTrailer
        );
    }

    // Buffered responses capture their trailers eagerly (the source HttpResponseMessage is disposed
    // before the closure runs). Trailer first, then a leading header — some HTTP stacks surface a
    // trailers-only gRPC response as an initial header block.
    private static string? GetCapturedTrailer(
        IReadOnlyDictionary<string, IReadOnlyList<string>> trailers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    ) =>
        trailers.TryGetValue(name, out var trailerValues) && trailerValues.Count > 0
            ? trailerValues[0]
        : headers.TryGetValue(name, out var headerValues) && headerValues.Count > 0
            ? headerValues[0]
        : null;

    private HttpRequestMessage CreateMessage(SmithyHttpRequest request)
    {
        var requestUri = HasDotPathSegment(request.RequestUri)
            ? new Uri(
                request.RequestUri,
                new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true }
            )
            : new Uri(request.RequestUri, UriKind.RelativeOrAbsolute);
        var message = new HttpRequestMessage(request.Method, requestUri)
        {
            // Honor the HttpClient's configured HTTP version/policy. A new HttpRequestMessage
            // defaults to HTTP/1.1, which would silently downgrade gRPC (HTTP/2) requests even when
            // the caller configured the client for HTTP/2.
            Version = httpClient.DefaultRequestVersion,
            VersionPolicy = httpClient.DefaultVersionPolicy,
            Content = CreateContent(request.Body, request.ContentType),
        };
        foreach (var header in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (message.Content is not null && request.ExistingContentHeaders is { } contentHeaders)
        {
            foreach (var header in contentHeaders)
            {
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return message;
    }

    private static bool HasDotPathSegment(string uri)
    {
        var pathEnd = uri.IndexOfAny(['?', '#']);
        var path = pathEnd < 0 ? uri : uri[..pathEnd];
        return path.Split('/').Any(segment => segment is "." or "..");
    }

    private static HttpContent? CreateContent(SmithyHttpBody body, string? contentType) =>
        body switch
        {
            SmithyHttpBody.EventStreaming eventStream => new ChunkedBodyContent(
                eventStream.Content,
                contentType
            ),
            SmithyHttpBody.Streaming streaming => WithContentType(
                CreateStreamContent(streaming),
                contentType
            ),
            SmithyHttpBody.Bytes bytes => WithContentType(
                new ByteArrayContent(bytes.Content),
                contentType
            ),
            _ => null,
        };

    private static StreamContent CreateStreamContent(SmithyHttpBody.Streaming streaming)
    {
        var content = new StreamContent(streaming.Content);
        if (streaming.ContentLength is { } contentLength)
        {
            content.Headers.ContentLength = contentLength;
        }

        return content;
    }

    private static HttpContent WithContentType(HttpContent content, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        return content;
    }

    private static string? GetTrailer(HttpResponseMessage response, string name)
    {
        if (response.TrailingHeaders.TryGetValues(name, out var trailers))
        {
            return trailers.FirstOrDefault();
        }

        // Some HTTP stacks surface a trailers-only gRPC response as an initial header block.
        return response.Headers.TryGetValues(name, out var headers)
            ? headers.FirstOrDefault()
            : null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToHeaderDictionary(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers
    )
    {
        Dictionary<string, IReadOnlyList<string>>? result = null;
        foreach (var header in headers)
        {
            result ??= new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase
            );
            result[header.Key] = header.Value.ToArray();
        }

        return result ?? NoHeaders;
    }

    private sealed class ChunkedBodyContent : HttpContent
    {
        private readonly IAsyncEnumerable<ReadOnlyMemory<byte>> chunks;

        public ChunkedBodyContent(
            IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
            string? contentType
        )
        {
            this.chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken
        )
        {
            await foreach (
                var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false)
            )
            {
                await stream.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ResponseOwningStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            response.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
