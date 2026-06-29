namespace NSmithy.Client;

/// <summary>
/// <c>smithy.api#httpBearerAuth</c>: sends the token in an <c>Authorization: Bearer ...</c> header.
/// </summary>
public sealed class HttpBearerAuthScheme(string token) : ISmithyAuthScheme
{
    private readonly string token = string.IsNullOrWhiteSpace(token)
        ? throw new ArgumentException("Token must be set.", nameof(token))
        : token;

    public string SchemeId => AuthSchemeIds.HttpBearerAuth;

    public IClientInterceptor CreateInterceptor(SmithyAuthSchemeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CreateAuthHandler();
    }

    private HeaderAuthInterceptor CreateAuthHandler()
    {
        return new HeaderAuthInterceptor("Authorization", $"Bearer {token}");
    }
}
