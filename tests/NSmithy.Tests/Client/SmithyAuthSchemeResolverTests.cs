using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Client;

public sealed class SmithyAuthSchemeResolverTests
{
    private static readonly Uri Endpoint = new("http://localhost:4566");
    private static readonly ServiceSchema Service = Schemas.Service(new ShapeId("example", "Svc"));

    [Fact]
    public void ResolveWithoutAuthSchemesReturnsInterceptorsOnly()
    {
        var existing = new MarkerInterceptor("existing");

        var resolved = SmithyAuthSchemeResolver.Resolve(
            Endpoint,
            Service,
            ["aws.auth#sigv4"],
            authSchemes: null,
            interceptors: [existing]
        );

        Assert.Equal([existing], resolved);
    }

    [Fact]
    public void ResolvePicksFirstModeledSchemeRegardlessOfConfiguredOrder()
    {
        // Configured in B, A order; modeled prefers A — modeled order wins.
        var schemeA = new FakeScheme("scheme#a", new MarkerInterceptor("a"));
        var schemeB = new FakeScheme("scheme#b", new MarkerInterceptor("b"));

        var resolved = SmithyAuthSchemeResolver.Resolve(
            Endpoint,
            Service,
            ["scheme#a", "scheme#b"],
            authSchemes: [schemeB, schemeA]
        );

        Assert.Equal("a", Assert.IsType<MarkerInterceptor>(Assert.Single(resolved)).Tag);
    }

    [Fact]
    public void ResolveSkipsModeledSchemesWithoutAConfiguredMatch()
    {
        var schemeB = new FakeScheme("scheme#b", new MarkerInterceptor("b"));

        var resolved = SmithyAuthSchemeResolver.Resolve(
            Endpoint,
            Service,
            ["scheme#a", "scheme#b"],
            authSchemes: [schemeB]
        );

        Assert.Equal("b", Assert.IsType<MarkerInterceptor>(Assert.Single(resolved)).Tag);
    }

    [Fact]
    public void ResolveAppendsAuthAfterUserInterceptors()
    {
        var existing = new MarkerInterceptor("existing");
        var auth = new MarkerInterceptor("auth");
        var scheme = new FakeScheme("scheme#a", auth);

        var resolved = SmithyAuthSchemeResolver.Resolve(
            Endpoint,
            Service,
            ["scheme#a"],
            authSchemes: [scheme],
            interceptors: [existing]
        );

        Assert.Equal(2, resolved.Count);
        Assert.Equal("existing", Assert.IsType<MarkerInterceptor>(resolved[0]).Tag);
        Assert.Equal("auth", Assert.IsType<MarkerInterceptor>(resolved[1]).Tag);
    }

    [Fact]
    public void ResolveThrowsWhenNoConfiguredSchemeMatchesModeledSchemes()
    {
        var scheme = new FakeScheme("scheme#c", new MarkerInterceptor("c"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            SmithyAuthSchemeResolver.Resolve(
                Endpoint,
                Service,
                ["scheme#a", "scheme#b"],
                authSchemes: [scheme]
            )
        );

        Assert.Contains("scheme#c", error.Message, StringComparison.Ordinal);
        Assert.Contains("scheme#a", error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeScheme(string schemeId, IClientInterceptor interceptor)
        : ISmithyAuthScheme
    {
        public string SchemeId => schemeId;

        public IClientInterceptor CreateInterceptor(SmithyAuthSchemeContext context) => interceptor;
    }

    private sealed class MarkerInterceptor(string tag) : IClientInterceptor
    {
        public string Tag => tag;
    }
}
