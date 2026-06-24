using System.Text;

namespace NSmithy.Client;

/// <summary>
/// <c>smithy.api#httpBasicAuth</c>: sends base64-encoded <c>username:password</c> in an
/// <c>Authorization: Basic ...</c> header.
/// </summary>
public sealed class HttpBasicAuthScheme(string username, string password) : ISmithyAuthScheme
{
    private readonly string username = string.IsNullOrWhiteSpace(username)
        ? throw new ArgumentException("Username must be set.", nameof(username))
        : username;

    private readonly string password =
        password ?? throw new ArgumentNullException(nameof(password));

    public string SchemeId => AuthSchemeIds.HttpBasicAuth;

    public ISmithyClientMiddleware CreateMiddleware(SmithyAuthSchemeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return new HeaderAuthMiddleware("Authorization", $"Basic {credentials}");
    }
}
