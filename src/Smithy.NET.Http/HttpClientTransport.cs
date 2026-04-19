using System.Text;

namespace Smithy.NET.Http;

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
        using var message = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            message.Content = new StringContent(
                request.Content,
                Encoding.UTF8,
                request.ContentType ?? "application/octet-stream"
            );
            foreach (var header in request.ContentHeaders)
            {
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var content = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new SmithyHttpResponse(
            response.StatusCode,
            response.ReasonPhrase,
            content,
            ToHeaderDictionary(response.Headers),
            response.Content is null
                ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                : ToHeaderDictionary(response.Content.Headers)
        );
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
