using System.Text;
using System.Net;

namespace SmithyNet.Http;

public sealed record SmithyHttpResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    byte[] Content,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders
)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;

    public string ContentText => Encoding.UTF8.GetString(Content);
}
