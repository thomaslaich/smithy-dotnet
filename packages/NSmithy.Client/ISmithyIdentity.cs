using NSmithy.Core;

namespace NSmithy.Client;

/// <summary>An identity used to authenticate a Smithy client request.</summary>
public interface ISmithyIdentity
{
    /// <summary>
    /// When the identity expires, or null when it does not expire. Caching resolvers use this to
    /// refresh an identity before it becomes unusable.
    /// </summary>
    DateTimeOffset? Expiration => null;
}

/// <summary>A token, API key, or encoded credential used by the built-in HTTP auth schemes.</summary>
public sealed class SmithyTokenIdentity : ISmithyIdentity
{
    public SmithyTokenIdentity(string value, DateTimeOffset? expiration = null)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Identity value must be set.", nameof(value))
            : value;
        Expiration = expiration;
    }

    public string Value { get; }

    public DateTimeOffset? Expiration { get; }
}

/// <summary>Resolves an identity for one operation invocation.</summary>
public interface ISmithyIdentityResolver
{
    ValueTask<ISmithyIdentity> ResolveIdentityAsync(
        SmithyIdentityProperties properties,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The operation and endpoint for which an identity is being resolved.</summary>
public sealed record SmithyIdentityProperties(
    ShapeId ServiceId,
    ShapeId OperationId,
    SmithyEndpoint? Endpoint
);

/// <summary>Returns one immutable identity for every request.</summary>
public sealed class StaticSmithyIdentityResolver(ISmithyIdentity identity) : ISmithyIdentityResolver
{
    private readonly ISmithyIdentity identity =
        identity ?? throw new ArgumentNullException(nameof(identity));

    public ValueTask<ISmithyIdentity> ResolveIdentityAsync(
        SmithyIdentityProperties properties,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(properties);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(identity);
    }
}
