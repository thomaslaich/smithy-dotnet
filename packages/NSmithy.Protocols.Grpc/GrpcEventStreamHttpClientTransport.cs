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
        var responseStream = response.Content is null
            ? Stream.Null
            : await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return new SmithyEventStreamHttpResponse(
            response.StatusCode,
            response.ReasonPhrase,
            ReadResponseEvents(response, responseStream, cancellationToken),
            ToHeaderDictionary(response.Headers),
            response.Content is null
                ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                : ToHeaderDictionary(response.Content.Headers)
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
