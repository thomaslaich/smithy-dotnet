using System.Net;
using System.Text;

namespace NSmithy.Http;

public sealed record SmithyHttpResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    byte[] Content,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders
)
{
    public string ContentText => Encoding.UTF8.GetString(Content);
}
