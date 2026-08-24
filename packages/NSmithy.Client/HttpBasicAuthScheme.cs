using System.Text;

namespace NSmithy.Client;

/// <summary>
/// <c>smithy.api#httpBasicAuth</c>: sends base64-encoded <c>username:password</c> in an
/// <c>Authorization: Basic ...</c> header.
/// </summary>
public sealed class HttpBasicAuthScheme : ISmithyAuthScheme
{
    public HttpBasicAuthScheme(string username, string password)
        : this(
            new StaticSmithyIdentityResolver(
                new SmithyTokenIdentity(
                    Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(
                            $"{(string.IsNullOrWhiteSpace(username) ? throw new ArgumentException("Username must be set.", nameof(username)) : username)}:{password ?? throw new ArgumentNullException(nameof(password))}"
                        )
                    )
                )
            )
        ) { }

    private HttpBasicAuthScheme(ISmithyIdentityResolver identityResolver)
    {
        IdentityResolver =
            identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
    }

    public string SchemeId => AuthSchemeIds.HttpBasicAuth;

    public ISmithyIdentityResolver IdentityResolver { get; }

    public ISmithySigner Signer { get; } = new HeaderAuthSigner("Authorization", "Basic");
}
