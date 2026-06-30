namespace NSmithy.Client;

public static class SmithyContextKeys
{
    public static ContextKey<string> ServiceName { get; } = new("smithy.serviceName");

    public static ContextKey<string> OperationName { get; } = new("smithy.operationName");

    public static ContextKey<Uri> Endpoint { get; } = new("smithy.endpoint");

    public static ContextKey<int> Attempt { get; } = new("smithy.attempt");
}
