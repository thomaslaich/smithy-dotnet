using NSmithy.Http;

namespace NSmithy.Tests.Http;

public sealed class SmithyHttpVersionPreferenceTests
{
    [Fact]
    public void AppliesPreferredVersionAndDowngradePolicy()
    {
        using var client = new HttpClient();
        var preference = new SmithyHttpVersionPreference(
            System.Net.HttpVersion.Version20,
            allowDowngrade: true
        );

        preference.Apply(client);

        Assert.Equal(System.Net.HttpVersion.Version20, client.DefaultRequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, client.DefaultVersionPolicy);
    }

    [Fact]
    public void ExactPreferenceDisallowsDowngrade()
    {
        using var client = new HttpClient();

        SmithyHttpVersionPreference.Http2.Apply(client);

        Assert.Equal(HttpVersionPolicy.RequestVersionExact, client.DefaultVersionPolicy);
    }
}
