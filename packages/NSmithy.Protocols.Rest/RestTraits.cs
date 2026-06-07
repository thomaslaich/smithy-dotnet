using NSmithy.Core;

namespace NSmithy.Protocols.Rest;

public static class RestTraits
{
    private const string SmithyApi = "smithy.api";

    public static ShapeId Http { get; } = new(SmithyApi, "http");

    public static ShapeId HttpLabel { get; } = new(SmithyApi, "httpLabel");

    public static ShapeId HttpHeader { get; } = new(SmithyApi, "httpHeader");

    public static ShapeId HttpPrefixHeaders { get; } = new(SmithyApi, "httpPrefixHeaders");

    public static ShapeId HttpQuery { get; } = new(SmithyApi, "httpQuery");

    public static ShapeId HttpQueryParams { get; } = new(SmithyApi, "httpQueryParams");

    public static ShapeId HttpPayload { get; } = new(SmithyApi, "httpPayload");

    public static ShapeId HttpResponseCode { get; } = new(SmithyApi, "httpResponseCode");

    public static ShapeId TimestampFormat { get; } = new(SmithyApi, "timestampFormat");

    public static ShapeId MediaType { get; } = new(SmithyApi, "mediaType");

    public static Trait HttpTrait(string method, string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        return new Trait(
            Http,
            Document.From(
                new Dictionary<string, Document>(StringComparer.Ordinal)
                {
                    ["method"] = Document.From(method),
                    ["uri"] = Document.From(uri),
                }
            )
        );
    }

    public static Trait HttpLabelTrait { get; } = new(HttpLabel);

    public static Trait HttpHeaderTrait(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Trait(HttpHeader, Document.From(name));
    }

    public static Trait HttpPrefixHeadersTrait(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return new Trait(HttpPrefixHeaders, Document.From(prefix));
    }

    public static Trait HttpQueryTrait(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Trait(HttpQuery, Document.From(name));
    }

    public static Trait HttpQueryParamsTrait { get; } = new(HttpQueryParams);

    public static Trait HttpPayloadTrait { get; } = new(HttpPayload);

    public static Trait HttpResponseCodeTrait { get; } = new(HttpResponseCode);

    public static Trait TimestampFormatTrait(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return new Trait(TimestampFormat, Document.From(format));
    }

    public static Trait MediaTypeTrait(string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new Trait(MediaType, Document.From(mediaType));
    }
}
