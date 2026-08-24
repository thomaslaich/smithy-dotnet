namespace NSmithy.Http;

/// <summary>
/// The HTTP version a protocol prefers and whether negotiation may fall back to an older version.
/// </summary>
public sealed record SmithyHttpVersionPreference
{
    public SmithyHttpVersionPreference(Version preferredVersion, bool allowDowngrade)
    {
        PreferredVersion =
            preferredVersion ?? throw new ArgumentNullException(nameof(preferredVersion));
        AllowDowngrade = allowDowngrade;
    }

    public Version PreferredVersion { get; }

    public bool AllowDowngrade { get; }

    public HttpVersionPolicy VersionPolicy =>
        AllowDowngrade
            ? HttpVersionPolicy.RequestVersionOrLower
            : HttpVersionPolicy.RequestVersionExact;

    public static SmithyHttpVersionPreference Http11 { get; } =
        new(System.Net.HttpVersion.Version11, allowDowngrade: false);

    public static SmithyHttpVersionPreference Http2 { get; } =
        new(System.Net.HttpVersion.Version20, allowDowngrade: false);

    public void Apply(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestVersion = PreferredVersion;
        client.DefaultVersionPolicy = VersionPolicy;
    }
}
