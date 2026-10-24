using System.Text.Json.Nodes;

namespace Endpoints.Conformance;

/// <summary>
/// The <c>@endpointTests</c> cases of every service in the assembled model (smithy-model.json),
/// with the rule parameters each service declares.
/// </summary>
internal sealed class EndpointTestModel
{
    private static readonly Lazy<EndpointTestModel> Instance = new(Load);

    private readonly Dictionary<string, JsonObject> services;

    private EndpointTestModel(Dictionary<string, JsonObject> services) => this.services = services;

    public static EndpointTestModel Current => Instance.Value;

    public IEnumerable<string> ServiceIds => services.Keys.Order(StringComparer.Ordinal);

    public JsonArray TestCases(string serviceId) =>
        Traits(serviceId)["smithy.rules#endpointTests"]!["testCases"]!.AsArray();

    /// <summary>Rule parameters by name, from the BDD when present, otherwise the rule set.</summary>
    public JsonObject Parameters(string serviceId)
    {
        var traits = Traits(serviceId);
        var rules = traits["smithy.rules#endpointBdd"] ?? traits["smithy.rules#endpointRuleSet"];
        return rules!["parameters"]!.AsObject();
    }

    /// <summary>Names of the service's <c>@clientContextParams</c>.</summary>
    public IEnumerable<string> ClientContextParameters(string serviceId) =>
        Traits(serviceId)["smithy.rules#clientContextParams"]?.AsObject().Select(p => p.Key) ?? [];

    private JsonObject Traits(string serviceId) => services[serviceId]["traits"]!.AsObject();

    private static EndpointTestModel Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "smithy-model.json");
        var shapes = JsonNode.Parse(File.ReadAllText(path))!["shapes"]!.AsObject();
        var services = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (id, shape) in shapes)
        {
            if (
                shape is JsonObject obj
                && (string?)obj["type"] == "service"
                && obj["traits"]?["smithy.rules#endpointTests"] is not null
            )
            {
                services[id] = obj;
            }
        }
        return new EndpointTestModel(services);
    }
}
