namespace NSmithy.Client;

/// <summary>
/// <c>smithy.api#httpBearerAuth</c>: sends the token in an <c>Authorization: Bearer ...</c> header.
/// </summary>
public sealed class HttpBearerAuthScheme : ISmithyAuthScheme
{
    public HttpBearerAuthScheme(string token)
        : this(
            new StaticSmithyIdentityResolver(
                new SmithyTokenIdentity(
                    string.IsNullOrWhiteSpace(token)
                        ? throw new ArgumentException("Token must be set.", nameof(token))
                        : token
                )
            )
        ) { }

    public HttpBearerAuthScheme(ISmithyIdentityResolver identityResolver)
    {
        IdentityResolver =
            identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
    }

    public string SchemeId => AuthSchemeIds.HttpBearerAuth;

    public ISmithyIdentityResolver IdentityResolver { get; }

    public ISmithySigner Signer { get; } = new HeaderAuthSigner("Authorization", "Bearer");
}
