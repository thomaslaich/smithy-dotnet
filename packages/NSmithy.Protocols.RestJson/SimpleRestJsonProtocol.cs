using System.Text.Json;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestJson;

/// <summary>
/// The alloy <c>simpleRestJson</c> protocol. It shares restJson1's JSON body format and HTTP binding
/// rules but differs in two wire details: string/enum <c>@httpPayload</c> members are JSON-encoded
/// (not raw <c>text/plain</c>), and the modeled-error discriminator travels in the <c>X-Error-Type</c>
/// header rather than <c>X-Amzn-Errortype</c>.
/// </summary>
public static class SimpleRestJsonProtocol
{
    private const string ErrorTypeHeader = "X-Error-Type";

    public static IServiceProtocol ForService(ServiceSchema service) =>
        new RestServiceProtocol(
            JsonRestBodyCodecFactory.Instance,
            DeserializeErrorType,
            rawStringPayloads: false,
            errorTypeHeader: ErrorTypeHeader
        );

    public static string? DeserializeErrorType(SmithyHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var headerValue =
            TryGetFirstHeaderValue(response.Headers, ErrorTypeHeader)
            ?? TryGetFirstHeaderValue(response.ContentHeaders, ErrorTypeHeader);
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            return NormalizeErrorType(headerValue);
        }

        if (response.Content.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("__type", out var dunderType)
                && dunderType.ValueKind == JsonValueKind.String
            )
            {
                return NormalizeErrorType(dunderType.GetString());
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? TryGetFirstHeaderValue(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        foreach (var header in headers)
        {
            if (
                string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
            )
            {
                return header.Value[0];
            }
        }

        return null;
    }

    private static string NormalizeErrorType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value;
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            text = text[..colon];
        }

        var hash = text.LastIndexOf('#');
        if (hash >= 0)
        {
            text = text[(hash + 1)..];
        }

        return text;
    }
}
