using SmithyNet.CodeGeneration.Model;
using SmithyNet.Core;

namespace SmithyNet.Tests.Protocol;

internal static class HttpProtocolComplianceCases
{
    private static readonly ShapeId HttpRequestTestsTrait = ShapeId.Parse(
        "smithy.test#httpRequestTests"
    );
    private static readonly ShapeId HttpResponseTestsTrait = ShapeId.Parse(
        "smithy.test#httpResponseTests"
    );

    public static IReadOnlyList<HttpRequestTestCase> ReadRequestTests(
        SmithyModel model,
        ShapeId operationId
    )
    {
        var operation = model.GetShape(operationId);
        return operation.Traits.GetValueOrDefault(HttpRequestTestsTrait) is { } value
            ? [.. value.AsArray().Select(ReadRequestTest)]
            : [];
    }

    public static IReadOnlyList<HttpResponseTestCase> ReadResponseTests(
        SmithyModel model,
        ShapeId operationId
    )
    {
        var operation = model.GetShape(operationId);
        return operation.Traits.GetValueOrDefault(HttpResponseTestsTrait) is { } value
            ? [.. value.AsArray().Select(ReadResponseTest)]
            : [];
    }

    private static HttpRequestTestCase ReadRequestTest(Document value)
    {
        var properties = value.AsObject();
        return new HttpRequestTestCase(
            ReadRequiredString(properties, "id"),
            ShapeId.Parse(ReadRequiredString(properties, "protocol")),
            ReadRequiredString(properties, "method"),
            ReadRequiredString(properties, "uri"),
            ReadStringList(properties, "queryParams"),
            ReadStringMap(properties, "headers"),
            ReadOptionalString(properties, "body"),
            properties.TryGetValue("params", out var parameters) ? parameters : Document.Null
        );
    }

    private static HttpResponseTestCase ReadResponseTest(Document value)
    {
        var properties = value.AsObject();
        return new HttpResponseTestCase(
            ReadRequiredString(properties, "id"),
            ShapeId.Parse(ReadRequiredString(properties, "protocol")),
            (int)ReadRequiredNumber(properties, "code"),
            ReadStringMap(properties, "headers"),
            ReadOptionalString(properties, "body"),
            properties.TryGetValue("params", out var parameters) ? parameters : Document.Null
        );
    }

    private static string ReadRequiredString(
        IReadOnlyDictionary<string, Document> properties,
        string name
    )
    {
        return properties.TryGetValue(name, out var value)
            ? value.AsString()
            : throw new InvalidOperationException($"Protocol test case is missing '{name}'.");
    }

    private static decimal ReadRequiredNumber(
        IReadOnlyDictionary<string, Document> properties,
        string name
    )
    {
        return properties.TryGetValue(name, out var value)
            ? value.AsNumber()
            : throw new InvalidOperationException($"Protocol test case is missing '{name}'.");
    }

    private static string? ReadOptionalString(
        IReadOnlyDictionary<string, Document> properties,
        string name
    )
    {
        return properties.TryGetValue(name, out var value) ? value.AsString() : null;
    }

    private static IReadOnlyList<string> ReadStringList(
        IReadOnlyDictionary<string, Document> properties,
        string name
    )
    {
        return properties.TryGetValue(name, out var value)
            ? [.. value.AsArray().Select(item => item.AsString())]
            : [];
    }

    private static Dictionary<string, string> ReadStringMap(
        IReadOnlyDictionary<string, Document> properties,
        string name
    )
    {
        return properties.TryGetValue(name, out var value)
            ? value
                .AsObject()
                .ToDictionary(
                    item => item.Key,
                    item => item.Value.AsString(),
                    StringComparer.OrdinalIgnoreCase
                )
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record HttpRequestTestCase(
    string Id,
    ShapeId Protocol,
    string Method,
    string Uri,
    IReadOnlyList<string> QueryParams,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    Document Parameters
);

internal sealed record HttpResponseTestCase(
    string Id,
    ShapeId Protocol,
    int Code,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    Document Parameters
);
