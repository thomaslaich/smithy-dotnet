using SmithyNet.Core;

namespace SmithyNet.CodeGeneration.Model;

public static class SmithyPrelude
{
    public const string Namespace = "smithy.api";

    public const string AwsProtocolsNamespace = "aws.protocols";

    public const string AlloyNamespace = "alloy";

    public static ShapeId RequiredTrait { get; } = new(Namespace, "required");

    public static ShapeId DefaultTrait { get; } = new(Namespace, "default");

    public static ShapeId ClientOptionalTrait { get; } = new(Namespace, "clientOptional");

    public static ShapeId InputTrait { get; } = new(Namespace, "input");

    public static ShapeId OutputTrait { get; } = new(Namespace, "output");

    public static ShapeId ErrorTrait { get; } = new(Namespace, "error");

    public static ShapeId HttpTrait { get; } = new(Namespace, "http");

    public static ShapeId HttpHeaderTrait { get; } = new(Namespace, "httpHeader");

    public static ShapeId HttpLabelTrait { get; } = new(Namespace, "httpLabel");

    public static ShapeId HttpPayloadTrait { get; } = new(Namespace, "httpPayload");

    public static ShapeId HttpPrefixHeadersTrait { get; } = new(Namespace, "httpPrefixHeaders");

    public static ShapeId HttpQueryTrait { get; } = new(Namespace, "httpQuery");

    public static ShapeId HttpQueryParamsTrait { get; } = new(Namespace, "httpQueryParams");

    public static ShapeId HttpErrorTrait { get; } = new(Namespace, "httpError");

    public static ShapeId HttpResponseCodeTrait { get; } = new(Namespace, "httpResponseCode");

    public static ShapeId RestJson1Trait { get; } = new(AwsProtocolsNamespace, "restJson1");

    public static ShapeId SimpleRestJsonTrait { get; } = new(AlloyNamespace, "simpleRestJson");

    public static ShapeId EnumValueTrait { get; } = new(Namespace, "enumValue");

    public static ShapeId JsonNameTrait { get; } = new(Namespace, "jsonName");

    public static ShapeId SparseTrait { get; } = new(Namespace, "sparse");
}
