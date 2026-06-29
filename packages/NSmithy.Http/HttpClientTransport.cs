namespace NSmithy.Http;

public sealed class HttpClientTransport : IHttpTransport
{
    private readonly HttpClient httpClient;

    public HttpClientTransport(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public Task<SmithyHttpResponse> SendAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendCoreAsync(request, cancellationToken);
    }

    private async Task<SmithyHttpResponse> SendCoreAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken
    )
    {
        using var message = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            // Honor the HttpClient's configured HTTP version/policy. A new HttpRequestMessage
            // defaults to HTTP/1.1, which would silently downgrade gRPC (HTTP/2) requests even when
            // the caller configured the client for HTTP/2.
            Version = httpClient.DefaultRequestVersion,
            VersionPolicy = httpClient.DefaultVersionPolicy,
        };
        foreach (var header in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.StreamingContent is not null)
        {
            message.Content = new StreamContent(request.StreamingContent);
            if (request.StreamingContentLength is { } contentLength)
            {
                message.Content.Headers.ContentLength = contentLength;
            }
        }
        else if (request.Content is not null)
        {
            message.Content = new ByteArrayContent(request.Content);
        }

        if (message.Content is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                message.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
            }

            foreach (var header in request.ContentHeaders)
            {
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (request.ExpectStreamingResponse)
        {
            var contentHeaders = response.Content is null
                ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                : ToHeaderDictionary(response.Content.Headers);
            return new SmithyHttpResponse(
                response.StatusCode,
                response.ReasonPhrase,
                [],
                ToHeaderDictionary(response.Headers),
                contentHeaders
            )
            {
                StreamingContent = new ResponseContentStream(
                    response,
                    response.Content is null
                        ? Stream.Null
                        : await response
                            .Content.ReadAsStreamAsync(cancellationToken)
                            .ConfigureAwait(false)
                ),
            };
        }

        using var bufferedResponse = response;
        var content = response.Content is null
            ? []
            : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // gRPC carries grpc-status / grpc-message in HTTP/2 trailers, which only become available
        // after the body has been read. Fold them into the header dictionary so protocols that look
        // for trailers (GrpcProtocol) see them uniformly with regular headers.
        var headers = ToHeaderDictionary(response.Headers);
        MergeTrailingHeaders(headers, response);
        return new SmithyHttpResponse(
            response.StatusCode,
            response.ReasonPhrase,
            content,
            headers,
            response.Content is null
                ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                : ToHeaderDictionary(response.Content.Headers)
        );
    }

    private static void MergeTrailingHeaders(
        Dictionary<string, IReadOnlyList<string>> headers,
        HttpResponseMessage response
    )
    {
        foreach (var trailer in response.TrailingHeaders)
        {
            headers[trailer.Key] = trailer.Value.ToArray();
        }
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

    private sealed class ResponseContentStream(HttpResponseMessage response, Stream inner) : Stream
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
