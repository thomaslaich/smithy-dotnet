using System.Net;
using System.Text;

namespace NSmithy.Http;

public sealed record SmithyHttpClientResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    SmithyHttpBody Body,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders,
    Func<string, string?>? Trailer = null
)
{
    public SmithyHttpClientResponse(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        byte[] content,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> contentHeaders
    )
        : this(
            statusCode,
            reasonPhrase,
            content.Length == 0 ? SmithyHttpBody.Empty : new SmithyHttpBody.Bytes(content),
            headers,
            contentHeaders,
            null
        ) { }

    public SmithyHttpClientResponse(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        Stream content,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> contentHeaders,
        Func<string, string?>? trailer = null,
        long? contentLength = null
    )
        : this(
            statusCode,
            reasonPhrase,
            new SmithyHttpBody.Streaming(content, contentLength),
            headers,
            contentHeaders,
            trailer
        ) { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "SmithyHttpClientResponse exposes buffered wire bytes for protocol codecs."
    )]
    public byte[] Content => Body is SmithyHttpBody.Bytes bytes ? bytes.Content : [];

    public string ContentText => Encoding.UTF8.GetString(Content);
}
