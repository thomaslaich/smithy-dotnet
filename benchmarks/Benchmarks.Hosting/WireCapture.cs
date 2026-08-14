using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bench.Corpus;

namespace Bench.Hosting;

/// <summary>
/// A stack's response to one corpus scenario, reduced to the parts that define
/// the wire contract.
/// </summary>
/// <remarks>
/// Transport-level headers (<c>Date</c>, <c>Server</c>, <c>Content-Length</c>,
/// framing headers) are excluded: they vary by host and by run without saying
/// anything about whether two stacks serve the same contract. Everything that
/// remains is contract.
/// <para>
/// Large bodies are recorded as a hash plus a length rather than inline, so the
/// golden files stay reviewable in a diff. The comparison is exact either way.
/// </para>
/// </remarks>
public sealed record WireCapture(
    [property: JsonPropertyName("scenario")] string Scenario,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("headers")] SortedDictionary<string, string> Headers,
    [property: JsonPropertyName("bodyLength")] int BodyLength,
    [property: JsonPropertyName("bodySha256")] string BodySha256,
    [property: JsonPropertyName("body")] string? Body
)
{
    /// <summary>Bodies at or below this size are stored inline for readable diffs.</summary>
    private const int InlineBodyLimit = 8 * 1024;

    private static readonly HashSet<string> IgnoredHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Date",
        "Server",
        "Content-Length",
        "Transfer-Encoding",
        "Connection",
        "Keep-Alive",
    };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Sends one scenario at one stack and reduces the response to a capture.</summary>
    public static async Task<WireCapture> CaptureAsync(
        BenchServer server,
        BenchRequest request,
        CancellationToken cancellationToken = default
    )
    {
        using var message = BenchServer.BuildRequest(request);
        using var response = await server.Client.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (IgnoredHeaders.Contains(header.Key))
                continue;

            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new WireCapture(
            Scenario: request.Name,
            Status: (int)response.StatusCode,
            Headers: headers,
            BodyLength: body.Length,
            BodySha256: Convert.ToHexStringLower(SHA256.HashData(body)),
            Body: body.Length <= InlineBodyLimit ? Encoding.UTF8.GetString(body) : null
        );
    }

    /// <summary>
    /// A one-line human-readable summary, used in parity failure messages where
    /// dumping two full captures would bury the actual difference.
    /// </summary>
    public string Describe() =>
        $"{Scenario}: {Status}, {BodyLength} bytes, sha={BodySha256[..12]}, "
        + $"headers=[{string.Join(", ", Headers.Select(h => $"{h.Key}={h.Value}"))}]";

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static WireCapture FromJson(string json) =>
        JsonSerializer.Deserialize<WireCapture>(json, JsonOptions)
        ?? throw new InvalidOperationException("Golden capture deserialized to null.");
}
