namespace NSmithy.Client;

using NSmithy.Core;

public static class SmithyContextKeys
{
    public static ContextKey<string> ServiceName { get; } = new("smithy.serviceName");

    public static ContextKey<ShapeId> ServiceId { get; } = new("smithy.serviceId");

    public static ContextKey<string> OperationName { get; } = new("smithy.operationName");

    public static ContextKey<ShapeId> OperationId { get; } = new("smithy.operationId");

    public static ContextKey<Uri> Endpoint { get; } = new("smithy.endpoint");

    public static ContextKey<SmithyEndpoint> ResolvedEndpoint { get; } =
        new("smithy.resolvedEndpoint");

    public static ContextKey<string> AuthSchemeId { get; } = new("smithy.authSchemeId");

    public static ContextKey<int> Attempt { get; } = new("smithy.attempt");
}
