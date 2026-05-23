namespace NSmithy.Client;

public sealed class SmithyClientOptions
{
    public static SmithyClientOptions Default { get; } = new();

    public Uri? Endpoint { get; init; }

    public IReadOnlyList<ISmithyClientMiddleware> Middleware { get; init; } = [];

    public Func<string> IdempotencyTokenProvider { get; init; } = static () =>
        Guid.NewGuid().ToString();
}
