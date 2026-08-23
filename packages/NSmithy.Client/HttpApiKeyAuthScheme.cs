namespace NSmithy.Client;

/// <summary>Where an API key credential is carried on the request.</summary>
public enum ApiKeyLocation
{
    Header,
    Query,
}

/// <summary>
/// <c>smithy.api#httpApiKeyAuth</c>: sends an API key in a named header or query parameter.
/// For header placement an optional <paramref name="scheme"/> prefixes the value (e.g.
/// <c>Authorization: ApiKey &lt;key&gt;</c>); the <c>name</c>, <c>in</c> and <c>scheme</c> values
/// come from the service's <c>@httpApiKeyAuth</c> trait.
/// </summary>
public sealed class HttpApiKeyAuthScheme : ISmithyAuthScheme
{
    private readonly string name;
    public HttpApiKeyAuthScheme(
        string name,
        string apiKey,
        ApiKeyLocation location = ApiKeyLocation.Header,
        string? scheme = null
    )
        : this(
            name,
            new StaticSmithyIdentityResolver(
                new SmithyTokenIdentity(
                    string.IsNullOrWhiteSpace(apiKey)
                        ? throw new ArgumentException("API key must be set.", nameof(apiKey))
                        : apiKey
                )
            ),
            location,
            scheme
        ) { }

    public HttpApiKeyAuthScheme(
        string name,
        ISmithyIdentityResolver identityResolver,
        ApiKeyLocation location = ApiKeyLocation.Header,
        string? scheme = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (location is ApiKeyLocation.Query && scheme is not null)
        {
            throw new ArgumentException(
                "A scheme prefix is only valid for header API keys.",
                nameof(scheme)
            );
        }

        this.name = name;
        IdentityResolver =
            identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        Signer = location switch
        {
            ApiKeyLocation.Header => new HeaderAuthSigner(name, scheme),
            ApiKeyLocation.Query => new QueryParameterAuthSigner(name),
            _ => throw new ArgumentOutOfRangeException(nameof(location)),
        };
    }

    public string SchemeId => AuthSchemeIds.HttpApiKeyAuth;

    public ISmithyIdentityResolver IdentityResolver { get; }

    public ISmithySigner Signer { get; }
}
