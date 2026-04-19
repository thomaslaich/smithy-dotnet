namespace Smithy.NET.Client;

public sealed class SmithyClientOptions
{
    public static SmithyClientOptions Default { get; } = new();

    public Uri? Endpoint { get; init; }

    public IReadOnlyList<ISmithyClientMiddleware> Middleware { get; init; } = [];
}
