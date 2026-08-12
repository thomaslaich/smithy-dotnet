using System.Text.Json;
using System.Text.Json.Nodes;

namespace RestJson1.Conformance;

/// <summary>
/// One <c>smithy.test#httpMalformedRequestTests</c> case: a request the model forbids, and the
/// error response a conforming server owes for it. Unlike the request/response suites these are
/// server-only — there is no client behavior to assert, because a client would never send them.
/// </summary>
internal sealed record HttpMalformedRequestTestCase(
    string ShapeId,
    string Id,
    string Method,
    string Uri,
    IReadOnlyList<string> QueryParams,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    int ExpectedCode,
    IReadOnlyDictionary<string, string> ExpectedHeaders,
    string? ExpectedBodyMediaType,
    string? ExpectedBodyContents,
    string? ExpectedMessageRegex
)
{
    /// <summary>Operation local name (e.g. "MalformedLength" for "…#MalformedLength").</summary>
    public string OperationName => ShapeId.Split('#')[^1];

    /// <summary>
    /// Expands a case's <c>testParameters</c> into one case per value. Every parameter list has the
    /// same length; index <c>i</c> of each is substituted together, so the parameters describe one
    /// scenario across several fields rather than a cross product.
    /// </summary>
    public static IEnumerable<HttpMalformedRequestTestCase> Expand(string shapeId, JsonObject c)
    {
        var parameters = c["testParameters"] as JsonObject;
        if (parameters is null || parameters.Count == 0)
        {
            yield return From(shapeId, Rewrite(c, null, 0), (string)c["id"]!);
            yield break;
        }

        var count = parameters.First().Value!.AsArray().Count;
        for (var i = 0; i < count; i++)
        {
            // Cases share one modeled id, so index it to keep xUnit's case names distinct.
            yield return From(shapeId, Rewrite(c, parameters, i), $"{(string)c["id"]!}#{i}");
        }
    }

    private static JsonObject Rewrite(JsonObject c, JsonObject? parameters, int index) =>
        JsonNode.Parse(Substitute(c.ToJsonString(), parameters, index))!.AsObject();

    private static string Substitute(string json, JsonObject? parameters, int index)
    {
        foreach (var (name, values) in parameters ?? [])
        {
            var value = (string)values!.AsArray()[index]!;

            // The case is being rewritten as JSON text, so a literal has to be escaped for the
            // string it lands in, and a :S substitution additionally gains its own quotes.
            var literal = JsonEncode(value);
            json = json.Replace($"${name}:L", literal[1..^1], StringComparison.Ordinal);
            json = json.Replace($"${name}:S", JsonEncode(literal)[1..^1], StringComparison.Ordinal);
        }

        // '$' introduces a substitution, so a literal one is written '$$' — including inside the
        // regular expressions these cases quote back in their expected messages.
        return json.Replace("$$", "$", StringComparison.Ordinal);
    }

    private static string JsonEncode(string value) => JsonSerializer.Serialize(value);

    private static HttpMalformedRequestTestCase From(string shapeId, JsonObject c, string id)
    {
        var request = c["request"]!.AsObject();
        var response = c["response"]!.AsObject();
        var body = response["body"] as JsonObject;
        var assertion = body?["assertion"] as JsonObject;

        return new HttpMalformedRequestTestCase(
            shapeId,
            id,
            (string)request["method"]!,
            (string)request["uri"]!,
            ReadStringList(request, "queryParams"),
            ReadStringMap(request, "headers"),
            (string?)request["body"],
            (int)response["code"]!,
            ReadStringMap(response, "headers"),
            (string?)body?["mediaType"],
            (string?)assertion?["contents"],
            (string?)assertion?["messageRegex"]
        );
    }

    private static IReadOnlyList<string> ReadStringList(JsonObject o, string name) =>
        o[name] is JsonArray a ? [.. a.Select(n => (string)n!)] : [];

    private static Dictionary<string, string> ReadStringMap(JsonObject o, string name) =>
        o[name] is JsonObject m
            ? m.ToDictionary(
                kv => kv.Key,
                kv => (string)kv.Value!,
                StringComparer.OrdinalIgnoreCase
            )
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
