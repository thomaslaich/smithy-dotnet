using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Tests.Client;

public sealed class SmithyAuthSchemeResolverTests
{
    private static readonly ServiceSchema Service = Schemas.Service(new ShapeId("example", "Svc"));

    [Fact]
    public void ResolveSchemesWithoutAuthSchemesReturnsEmptyMap()
    {
        var resolved = SmithyAuthSchemeResolver.ResolveSchemes(
            Service,
            ["aws.auth#sigv4"],
            authSchemes: null
        );

        Assert.Empty(resolved);
    }

    [Fact]
    public void ResolveSchemesIndexesConfiguredSchemes()
    {
        var a = new FakeScheme("scheme#a", "a");
        var b = new FakeScheme("scheme#b", "b");

        var resolved = SmithyAuthSchemeResolver.ResolveSchemes(
            Service,
            ["scheme#a", "scheme#b"],
            authSchemes: [a, b]
        );

        Assert.Same(a, resolved["scheme#a"]);
        Assert.Same(b, resolved["scheme#b"]);
    }

    [Fact]
    public void ResolveSchemesThrowsWhenNoConfiguredSchemeMatchesServiceSchemes()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SmithyAuthSchemeResolver.ResolveSchemes(
                Service,
                ["scheme#a", "scheme#b"],
                authSchemes: [new FakeScheme("scheme#c", "c")]
            )
        );

        Assert.Contains("scheme#c", error.Message, StringComparison.Ordinal);
        Assert.Contains("scheme#a", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectPicksFirstModeledSchemeRegardlessOfConfiguredOrder()
    {
        var interceptors = Map(("scheme#b", "b"), ("scheme#a", "a"));

        var selected = SmithyAuthSchemeResolver.SelectScheme(
            ["scheme#a", "scheme#b"],
            endpointAuthSchemes: null,
            interceptors
        );

        Assert.Equal("a", Assert.IsType<FakeScheme>(selected).Tag);
    }

    [Fact]
    public void SelectSkipsModeledSchemesWithoutAConfiguredMatch()
    {
        var interceptors = Map(("scheme#b", "b"));

        var selected = SmithyAuthSchemeResolver.SelectScheme(
            ["scheme#a", "scheme#b"],
            endpointAuthSchemes: null,
            interceptors
        );

        Assert.Equal("b", Assert.IsType<FakeScheme>(selected).Tag);
    }

    [Fact]
    public void SelectReturnsNullForAnonymousOperations()
    {
        var interceptors = Map(("scheme#a", "a"));

        Assert.Null(
            SmithyAuthSchemeResolver.SelectScheme([], endpointAuthSchemes: null, interceptors)
        );
    }

    [Fact]
    public void SelectHonorsEndpointNarrowing()
    {
        var interceptors = Map(("scheme#a", "a"), ("scheme#b", "b"));

        var selected = SmithyAuthSchemeResolver.SelectScheme(
            ["scheme#a", "scheme#b"],
            endpointAuthSchemes: ["scheme#b"],
            interceptors
        );

        Assert.Equal("b", Assert.IsType<FakeScheme>(selected).Tag);
    }

    [Fact]
    public void SelectTreatsFullyNarrowedEndpointAsAnonymous()
    {
        var interceptors = Map(("scheme#a", "a"));

        Assert.Null(
            SmithyAuthSchemeResolver.SelectScheme(
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
            SmithyAuthSchemeResolver.SelectScheme(
                ["scheme#a"],
                endpointAuthSchemes: null,
                interceptors
            )
        );

        Assert.Contains("scheme#a", error.Message, StringComparison.Ordinal);
        Assert.Contains("scheme#c", error.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, ISmithyAuthScheme> Map(
        params (string SchemeId, string Tag)[] schemes
    )
    {
        var map = new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal);
        foreach (var (schemeId, tag) in schemes)
        {
            map[schemeId] = new FakeScheme(schemeId, tag);
        }

        return map;
    }

    private sealed class FakeScheme(string schemeId, string tag) : ISmithyAuthScheme
    {
        public string SchemeId => schemeId;

        public string Tag => tag;

        public ISmithyIdentityResolver IdentityResolver { get; } =
            new StaticSmithyIdentityResolver(new FakeIdentity());

        public ISmithySigner Signer { get; } = new FakeSigner();
    }

    private sealed class FakeIdentity : ISmithyIdentity;

    private sealed class FakeSigner : ISmithySigner
    {
        public ValueTask<SmithyHttpRequest> SignAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            ISmithyIdentity identity,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(request);
    }
}
