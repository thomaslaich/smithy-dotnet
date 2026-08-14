using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bench.Clients;

/// <summary>
/// What a client actually put on the wire, reduced to the parts that define the
/// request contract.
/// </summary>
/// <remarks>
/// The mirror of the server suite's wire capture: servers are pinned by the
/// responses they return, clients by the requests they emit. Without it, "client A
/// is faster" could just mean client A omits a header. Transport-managed headers
/// are excluded; query parameters are compared in emitted order, since ordering is
/// part of the bytes.
/// </remarks>
public sealed record CapturedRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("pathAndQuery")] string PathAndQuery,
    [property: JsonPropertyName("headers")] SortedDictionary<string, string> Headers,
    [property: JsonPropertyName("bodyLength")] int BodyLength,
    [property: JsonPropertyName("bodySha256")] string BodySha256,
    [property: JsonPropertyName("body")] string? Body
)
{
    private const int InlineBodyLimit = 8 * 1024;

    private static readonly HashSet<string> IgnoredHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Transfer-Encoding",
        "Connection",
        "Keep-Alive",
        "User-Agent",
        // Emitted by NSmithy's client runtime for distributed tracing. It carries
        // a fresh trace id per call, so it can never match across clients or runs
        // and is not part of the contract under test.
        "traceparent",
        "tracestate",
    };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CapturedRequest From(HttpRequestMessage request, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(body);

        var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var all = request.Headers.AsEnumerable();
        if (request.Content is not null)
            all = all.Concat(request.Content.Headers);

        foreach (var header in all)
        {
            if (IgnoredHeaders.Contains(header.Key))
                continue;

            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new CapturedRequest(
            Method: request.Method.Method,
            PathAndQuery: request.RequestUri?.PathAndQuery ?? "",
            Headers: headers,
            BodyLength: body.Length,
            BodySha256: Convert.ToHexStringLower(SHA256.HashData(body)),
            Body: body.Length is > 0 and <= InlineBodyLimit ? Encoding.UTF8.GetString(body) : null
        );
    }

    public string Describe() =>
        $"{Method} {PathAndQuery}, {BodyLength} bytes, sha={BodySha256[..12]}, "
        + $"headers=[{string.Join(", ", Headers.Select(h => $"{h.Key}={h.Value}"))}]";

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CapturedRequest FromJson(string json) =>
        JsonSerializer.Deserialize<CapturedRequest>(json, JsonOptions)
        ?? throw new InvalidOperationException("Golden request capture deserialized to null.");
}
