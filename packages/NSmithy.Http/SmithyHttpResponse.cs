using System.Net;
using System.Text;

namespace NSmithy.Http;

public sealed record SmithyHttpResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    SmithyHttpBody Body,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders
)
{
    public SmithyHttpResponse(
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
            contentHeaders
        ) { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "SmithyHttpResponse exposes buffered wire bytes for protocol codecs."
    )]
    public byte[] Content => Body is SmithyHttpBody.Bytes bytes ? bytes.Content : [];

    public string ContentText => Encoding.UTF8.GetString(Content);
}
