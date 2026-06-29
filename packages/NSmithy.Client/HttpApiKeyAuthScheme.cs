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
    private readonly string apiKey;
    private readonly ApiKeyLocation location;
    private readonly string? scheme;

    public HttpApiKeyAuthScheme(
        string name,
        string apiKey,
        ApiKeyLocation location = ApiKeyLocation.Header,
        string? scheme = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (location is ApiKeyLocation.Query && scheme is not null)
        {
            throw new ArgumentException(
                "A scheme prefix is only valid for header API keys.",
                nameof(scheme)
            );
        }

        this.name = name;
        this.apiKey = apiKey;
        this.location = location;
        this.scheme = scheme;
    }

    public string SchemeId => AuthSchemeIds.HttpApiKeyAuth;

    public IClientInterceptor CreateInterceptor(SmithyAuthSchemeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CreateAuthHandler();
    }

    private ISmithyAuthHandler CreateAuthHandler()
    {
        return location switch
        {
            ApiKeyLocation.Header => new HeaderAuthInterceptor(
                name,
                scheme is null ? apiKey : $"{scheme} {apiKey}"
            ),
            ApiKeyLocation.Query => new QueryParameterAuthInterceptor(name, apiKey),
            _ => throw new ArgumentOutOfRangeException(nameof(location)),
        };
    }
}
