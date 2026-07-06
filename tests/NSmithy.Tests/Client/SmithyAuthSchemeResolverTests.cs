using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Client;

public sealed class SmithyAuthSchemeResolverTests
{
    private static readonly Uri Endpoint = new("http://localhost:4566");
    private static readonly ServiceSchema Service = Schemas.Service(new ShapeId("example", "Svc"));

    [Fact]
    public void ResolveInterceptorsWithoutAuthSchemesReturnsEmptyMap()
    {
        var resolved = SmithyAuthSchemeResolver.ResolveInterceptors(
            Endpoint,
            Service,
            ["aws.auth#sigv4"],
            authSchemes: null
        );

        Assert.Empty(resolved);
    }

    [Fact]
    public void ResolveInterceptorsCreatesOneInterceptorPerConfiguredScheme()
    {
        var a = new MarkerInterceptor("a");
        var b = new MarkerInterceptor("b");

        var resolved = SmithyAuthSchemeResolver.ResolveInterceptors(
            Endpoint,
            Service,
            ["scheme#a", "scheme#b"],
            authSchemes: [new FakeScheme("scheme#a", a), new FakeScheme("scheme#b", b)]
        );

        Assert.Same(a, resolved["scheme#a"]);
        Assert.Same(b, resolved["scheme#b"]);
    }

    [Fact]
    public void ResolveInterceptorsThrowsWhenNoConfiguredSchemeMatchesServiceSchemes()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SmithyAuthSchemeResolver.ResolveInterceptors(
                Endpoint,
                Service,
                ["scheme#a", "scheme#b"],
                authSchemes: [new FakeScheme("scheme#c", new MarkerInterceptor("c"))]
            )
        );

        Assert.Contains("scheme#c", error.Message, StringComparison.Ordinal);
        Assert.Contains("scheme#a", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectPicksFirstModeledSchemeRegardlessOfConfiguredOrder()
    {
        var interceptors = Map(("scheme#b", "b"), ("scheme#a", "a"));

        var selected = SmithyAuthSchemeResolver.SelectInterceptor(
            ["scheme#a", "scheme#b"],
            endpointAuthSchemes: null,
            interceptors
        );

        Assert.Equal("a", Assert.IsType<MarkerInterceptor>(selected).Tag);
    }

    [Fact]
    public void SelectSkipsModeledSchemesWithoutAConfiguredMatch()
    {
        var interceptors = Map(("scheme#b", "b"));

        var selected = SmithyAuthSchemeResolver.SelectInterceptor(
            ["scheme#a", "scheme#b"],
            endpointAuthSchemes: null,
            interceptors
        );

        Assert.Equal("b", Assert.IsType<MarkerInterceptor>(selected).Tag);
    }

    [Fact]
    public void SelectReturnsNullForAnonymousOperations()
    {
        var interceptors = Map(("scheme#a", "a"));

        Assert.Null(
            SmithyAuthSchemeResolver.SelectInterceptor([], endpointAuthSchemes: null, interceptors)
        );
    }

    [Fact]
    public void SelectHonorsEndpointNarrowing()
    {
        var interceptors = Map(("scheme#a", "a"), ("scheme#b", "b"));

        var selected = SmithyAuthSchemeResolver.SelectInterceptor(
            ["scheme#a", "scheme#b"],
            endpointAuthSchemes: ["scheme#b"],
            interceptors
        );

        Assert.Equal("b", Assert.IsType<MarkerInterceptor>(selected).Tag);
    }

    [Fact]
    public void SelectTreatsFullyNarrowedEndpointAsAnonymous()
    {
        var interceptors = Map(("scheme#a", "a"));

        Assert.Null(
            SmithyAuthSchemeResolver.SelectInterceptor(
                ["scheme#a"],
                endpointAuthSchemes: ["scheme#other"],
                interceptors
            )
        );
    }

    [Fact]
    public void SelectThrowsWhenOperationSchemesHaveNoConfiguredMatch()
    {
        var interceptors = Map(("scheme#c", "c"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            SmithyAuthSchemeResolver.SelectInterceptor(
                ["scheme#a"],
                endpointAuthSchemes: null,
                interceptors
            )
        );

        Assert.Contains("scheme#a", error.Message, StringComparison.Ordinal);
        Assert.Contains("scheme#c", error.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, IClientInterceptor> Map(
        params (string SchemeId, string Tag)[] schemes
    )
    {
        var map = new Dictionary<string, IClientInterceptor>(StringComparer.Ordinal);
        foreach (var (schemeId, tag) in schemes)
        {
            map[schemeId] = new MarkerInterceptor(tag);
        }

        return map;
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
