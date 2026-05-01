using NSmithy.CodeGeneration.Model;
using NSmithy.Core;

namespace NSmithy.Tests.Protocol;

internal static class HttpProtocolComplianceCases
{
    private static readonly ShapeId HttpRequestTestsTrait = ShapeId.Parse(
        "smithy.test#httpRequestTests"
    );
    private static readonly ShapeId HttpResponseTestsTrait = ShapeId.Parse(
        "smithy.test#httpResponseTests"
    );
    private static readonly ShapeId HttpMalformedRequestTestsTrait = ShapeId.Parse(
        "smithy.test#httpMalformedRequestTests"
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

    public static IReadOnlyList<HttpMalformedRequestTestCase> ReadMalformedRequestTests(
        SmithyModel model,
        ShapeId operationId
    )
    {
        var operation = model.GetShape(operationId);
        return operation.Traits.GetValueOrDefault(HttpMalformedRequestTestsTrait) is { } value
            ? [.. value.AsArray().Select(ReadMalformedRequestTest)]
            : [];
    }

    private static HttpRequestTestCase ReadRequestTest(Document value)
    {
        var properties = value.AsObject();
        return new HttpRequestTestCase(
            ReadRequiredString(properties, "id"),
            ReadOptionalString(properties, "documentation"),
            ShapeId.Parse(ReadRequiredString(properties, "protocol")),
            ReadRequiredString(properties, "method"),
            ReadRequiredString(properties, "uri"),
            ReadStringList(properties, "queryParams"),
            ReadStringMap(properties, "headers"),
            ReadStringList(properties, "requireHeaders"),
            ReadStringList(properties, "forbidHeaders"),
            ReadStringList(properties, "requireQueryParams"),
            ReadStringList(properties, "forbidQueryParams"),
            ReadOptionalString(properties, "body"),
            ReadOptionalString(properties, "bodyMediaType"),
            ReadOptionalString(properties, "host"),
            ReadOptionalString(properties, "resolvedHost"),
            properties.TryGetValue("params", out var parameters) ? parameters : Document.Null
        );
    }

    private static HttpResponseTestCase ReadResponseTest(Document value)
    {
        var properties = value.AsObject();
        return new HttpResponseTestCase(
            ReadRequiredString(properties, "id"),
            ReadOptionalString(properties, "documentation"),
            ShapeId.Parse(ReadRequiredString(properties, "protocol")),
            (int)ReadRequiredNumber(properties, "code"),
            ReadStringMap(properties, "headers"),
            ReadOptionalString(properties, "body"),
            ReadOptionalString(properties, "bodyMediaType"),
            properties.TryGetValue("params", out var parameters) ? parameters : Document.Null
        );
    }

    private static HttpMalformedRequestTestCase ReadMalformedRequestTest(Document value)
    {
        var properties = value.AsObject();
        return new HttpMalformedRequestTestCase(
            ReadRequiredString(properties, "id"),
            ReadOptionalString(properties, "documentation"),
            ShapeId.Parse(ReadRequiredString(properties, "protocol")),
            ReadMalformedRequest(ReadRequiredDocument(properties, "request")),
            ReadMalformedResponse(ReadRequiredDocument(properties, "response")),
            properties.TryGetValue("testParameters", out var testParameters)
                ? testParameters
                : Document.Null,
            ReadStringList(properties, "tags")
        );
    }

    private static HttpMalformedRequestDefinition ReadMalformedRequest(Document value)
    {
        var properties = value.AsObject();
        return new HttpMalformedRequestDefinition(
            ReadRequiredString(properties, "method"),
            ReadRequiredString(properties, "uri"),
            ReadStringMap(properties, "headers"),
            ReadOptionalDocument(properties, "body"),
            ReadOptionalString(properties, "host")
        );
    }

    private static HttpMalformedResponseDefinition ReadMalformedResponse(Document value)
    {
        var properties = value.AsObject();
        return new HttpMalformedResponseDefinition(
            (int)ReadRequiredNumber(properties, "code"),
            ReadStringMap(properties, "headers"),
            ReadOptionalDocument(properties, "body"),
            ReadOptionalString(properties, "bodyMediaType")
        );
    }

    private static Document ReadRequiredDocument(
        IReadOnlyDictionary<string, Document> properties,
        string name
    )
    {
        return properties.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"Protocol test case is missing '{name}'.");
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

    private static Document? ReadOptionalDocument(
        IReadOnlyDictionary<string, Document> properties,
        string name
    )
    {
        return properties.TryGetValue(name, out var value) ? value : null;
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
    string? Documentation,
    ShapeId Protocol,
    string Method,
    string Uri,
    IReadOnlyList<string> QueryParams,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<string> RequireHeaders,
    IReadOnlyList<string> ForbidHeaders,
    IReadOnlyList<string> RequireQueryParams,
    IReadOnlyList<string> ForbidQueryParams,
    string? Body,
    string? BodyMediaType,
    string? Host,
    string? ResolvedHost,
    Document Parameters
);

internal sealed record HttpResponseTestCase(
    string Id,
    string? Documentation,
    ShapeId Protocol,
    int Code,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string? BodyMediaType,
    Document Parameters
);

internal sealed record HttpMalformedRequestTestCase(
    string Id,
    string? Documentation,
    ShapeId Protocol,
    HttpMalformedRequestDefinition Request,
    HttpMalformedResponseDefinition Response,
    Document TestParameters,
    IReadOnlyList<string> Tags
);

internal sealed record HttpMalformedRequestDefinition(
    string Method,
    string Uri,
    IReadOnlyDictionary<string, string> Headers,
    Document? Body,
    string? Host
);

internal sealed record HttpMalformedResponseDefinition(
    int Code,
    IReadOnlyDictionary<string, string> Headers,
    Document? Body,
    string? BodyMediaType
);
