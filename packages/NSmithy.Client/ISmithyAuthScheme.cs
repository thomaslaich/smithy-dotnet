using NSmithy.Core.Serde;

namespace NSmithy.Client;

/// <summary>
/// A client auth scheme. Each scheme is identified by the Smithy shape id of its auth trait
/// (e.g. <c>aws.auth#sigv4</c>, <c>smithy.api#httpBearerAuth</c>) so the resolver can match the
/// schemes a caller configured against the schemes the service models, in priority order.
/// </summary>
public interface ISmithyAuthScheme
{
    /// <summary>Absolute shape id of the auth trait this scheme implements.</summary>
    string SchemeId { get; }

    ISmithyClientMiddleware CreateMiddleware(SmithyAuthSchemeContext context);
}

public sealed record SmithyAuthSchemeContext(Uri Endpoint, ServiceSchema Service);
