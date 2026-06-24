namespace NSmithy.Client;

/// <summary>
/// Shape ids of the Smithy prelude HTTP auth traits, used as <see cref="ISmithyAuthScheme.SchemeId"/>
/// values. AWS auth scheme ids (e.g. <c>aws.auth#sigv4</c>) live with their implementations.
/// </summary>
public static class AuthSchemeIds
{
    public const string HttpBasicAuth = "smithy.api#httpBasicAuth";

    public const string HttpBearerAuth = "smithy.api#httpBearerAuth";

    public const string HttpApiKeyAuth = "smithy.api#httpApiKeyAuth";

    public const string HttpDigestAuth = "smithy.api#httpDigestAuth";
}
