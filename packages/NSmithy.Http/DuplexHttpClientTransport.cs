using System.Net;
using System.Net.Http.Headers;

namespace NSmithy.Http;

/// <summary>
/// The <see cref="HttpClient"/>-backed duplex transport. Writes the request's protocol-framed
/// body chunks (flushing after each), and returns the raw response stream for the protocol to
/// deframe. Protocol-neutral: it never inspects or produces framing, and it surfaces HTTP
/// trailers through <see cref="SmithyDuplexHttpResponse.Trailer"/> once the body is read to end.
/// Disposing the response body releases the underlying connection.
/// </summary>
public sealed class DuplexHttpClientTransport : IDuplexHttpTransport
{
    private readonly HttpClient httpClient;
    private readonly Uri? endpoint;

    public DuplexHttpClientTransport(HttpClient httpClient, Uri? endpoint = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (endpoint is not null && !endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));
        }

        this.endpoint = endpoint;
    }

    public async Task<SmithyDuplexHttpResponse> SendAsync(
        SmithyDuplexHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = new HttpRequestMessage(request.Method, ResolveRequestUri(request.RequestUri))
        {
            Version = httpClient.DefaultRequestVersion,
            VersionPolicy = httpClient.DefaultVersionPolicy,
            Content = new ChunkedBodyContent(request.Body, request.ContentType),
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

        var bodyStream = response.Content is null
            ? Stream.Null
            : await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return new SmithyDuplexHttpResponse(
            response.StatusCode,
            response.ReasonPhrase,
            new ResponseOwningStream(response, bodyStream),
            headers,
            contentHeaders,
            name => GetTrailerOrHeader(response, name)
        );
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
