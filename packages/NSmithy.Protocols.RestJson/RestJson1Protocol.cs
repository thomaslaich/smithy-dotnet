using System.Text.Json;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Rest;

namespace NSmithy.Protocols.RestJson;

public sealed class RestJson1Protocol : IProtocol
{
    /// <summary>
    /// Binds the protocol to a service, yielding per-operation protocols. REST derives each
    /// operation's binding from its <c>@http</c> trait, so the service schema is accepted for a
    /// uniform factory signature but not otherwise consulted. restJson1 writes string/enum payloads
    /// as raw text and signals modeled errors via <c>X-Amzn-Errortype</c>.
    /// </summary>
    public IServiceProtocol ForService(ServiceSchema service) =>
        new RestServiceProtocol(
            JsonRestBodyCodecFactory.Instance,
            DeserializeErrorType,
            rawStringPayloads: true,
            errorTypeHeader: "X-Amzn-Errortype"
        );

    public static string? DeserializeErrorType(SmithyHttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var headerValue =
            TryGetFirstHeaderValue(response.Headers, "X-Amzn-Errortype")
            ?? TryGetFirstHeaderValue(response.ContentHeaders, "X-Amzn-Errortype");
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
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (
                document.RootElement.TryGetProperty("__type", out var dunderType)
                && dunderType.ValueKind == JsonValueKind.String
            )
            {
                return NormalizeErrorType(dunderType.GetString());
            }

            if (
                document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
            )
            {
                return NormalizeErrorType(code.GetString());
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
