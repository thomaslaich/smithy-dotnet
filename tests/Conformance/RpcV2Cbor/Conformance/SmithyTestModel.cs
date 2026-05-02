using System.Text.Json;
using System.Text.Json.Nodes;

namespace RpcV2Cbor.Conformance;

/// <summary>
/// Minimal reader over the assembled Smithy AST (model.json) — exposes operation traits used by
/// the conformance suite (httpRequestTests, httpResponseTests). We deliberately don't model the
/// full AST; we only project the bits the runner needs.
/// </summary>
internal sealed class SmithyTestModel
{
    private readonly JsonObject shapes;

    private SmithyTestModel(JsonObject shapes)
    {
        this.shapes = shapes;
    }

    public JsonObject RawShapes => shapes;

    public static SmithyTestModel Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "smithy-model.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"smithy-model.json not found beside test assembly at {path}. "
                    + "Make sure RpcV2Cbor.Conformance.csproj copies it to output."
            );
        }
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return new SmithyTestModel(root["shapes"]!.AsObject());
    }

    public IEnumerable<HttpRequestTestCase> EnumerateHttpRequestTests(string protocol)
    {
        foreach (var (id, shape) in shapes)
        {
            if (shape is not JsonObject obj || (string?)obj["type"] != "operation")
                continue;
            var traits = obj["traits"] as JsonObject;
            var arr = traits?["smithy.test#httpRequestTests"] as JsonArray;
            if (arr is null)
                continue;
            foreach (var node in arr)
            {
                if (node is not JsonObject c || (string?)c["protocol"] != protocol)
                    continue;
                yield return HttpRequestTestCase.From(id, c);
            }
        }
    }

    public IEnumerable<HttpResponseTestCase> EnumerateHttpResponseTests(string protocol)
    {
        foreach (var (id, shape) in shapes)
        {
            if (shape is not JsonObject obj)
                continue;
            var type = (string?)obj["type"];
            if (type != "operation" && type != "structure")
                continue;
            var traits = obj["traits"] as JsonObject;
            var arr = traits?["smithy.test#httpResponseTests"] as JsonArray;
            if (arr is null)
                continue;
            foreach (var node in arr)
            {
                if (node is not JsonObject c || (string?)c["protocol"] != protocol)
                    continue;
                yield return HttpResponseTestCase.From(id, c);
            }
        }
    }
}

internal sealed record HttpRequestTestCase(
    string ShapeId,
    string Id,
    string Method,
    string Uri,
    IReadOnlyList<string> QueryParams,
    IReadOnlyList<string> RequireQueryParams,
    IReadOnlyList<string> ForbidQueryParams,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<string> RequireHeaders,
    IReadOnlyList<string> ForbidHeaders,
    string? Body,
    string? BodyMediaType,
    string? Host,
    string? ResolvedHost,
    JsonNode? Params
)
{
    /// <summary>Operation local name (e.g. "Health" for "alloy.test#Health").</summary>
    public string OperationName => ShapeId.Split('#')[^1];

    public static HttpRequestTestCase From(string shapeId, JsonObject c) =>
        new(
            shapeId,
            (string)c["id"]!,
            (string)c["method"]!,
            (string)c["uri"]!,
            ReadStringList(c, "queryParams"),
            ReadStringList(c, "requireQueryParams"),
            ReadStringList(c, "forbidQueryParams"),
            ReadStringMap(c, "headers"),
            ReadStringList(c, "requireHeaders"),
            ReadStringList(c, "forbidHeaders"),
            (string?)c["body"],
            (string?)c["bodyMediaType"],
            (string?)c["host"],
            (string?)c["resolvedHost"],
            c["params"]
        );

    private static IReadOnlyList<string> ReadStringList(JsonObject c, string key) =>
        c[key] is JsonArray a ? [.. a.Select(n => (string)n!)] : [];

    private static Dictionary<string, string> ReadStringMap(JsonObject c, string key) =>
        c[key] is JsonObject o ? o.ToDictionary(kv => kv.Key, kv => (string)kv.Value!) : [];
}

internal sealed record HttpResponseTestCase(
    string ShapeId,
    string Id,
    int Code,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string? BodyMediaType,
    JsonNode? Params
)
{
    public string OperationOrErrorName => ShapeId.Split('#')[^1];

    public static HttpResponseTestCase From(string shapeId, JsonObject c)
    {
        var headers = c["headers"] is JsonObject h
            ? h.ToDictionary(kv => kv.Key, kv => (string)kv.Value!)
            : new Dictionary<string, string>();
        return new HttpResponseTestCase(
            shapeId,
            (string)c["id"]!,
            (int)c["code"]!,
            headers,
            (string?)c["body"],
            (string?)c["bodyMediaType"],
            c["params"]
        );
    }
}
