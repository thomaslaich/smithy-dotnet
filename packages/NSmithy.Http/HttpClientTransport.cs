namespace NSmithy.Http;

public sealed class HttpClientTransport : IHttpTransport
{
    private readonly HttpClient httpClient;
    private readonly Uri? endpoint;

    public HttpClientTransport(HttpClient httpClient, Uri? endpoint = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (endpoint is not null && !endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));
        }

        this.endpoint = endpoint;
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
        using var message = new HttpRequestMessage(
            request.Method,
            ResolveRequestUri(request.RequestUri)
        )
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

        if (request.Content is not null)
        {
            message.Content = new ByteArrayContent(request.Content);
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

        using var response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
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
}
