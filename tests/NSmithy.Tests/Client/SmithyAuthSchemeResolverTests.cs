using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Client;

public sealed class SmithyAuthSchemeResolverTests
{
    private static readonly Uri Endpoint = new("http://localhost:4566");
    private static readonly ServiceSchema Service = Schemas.Service(new ShapeId("example", "Svc"));

    [Fact]
    public void ResolveWithoutAuthSchemesReturnsMiddlewareOnly()
    {
        var existing = new MarkerMiddleware("existing");

        var resolved = SmithyAuthSchemeResolver.Resolve(
            Endpoint,
            Service,
            ["aws.auth#sigv4"],
            authSchemes: null,
            middleware: [existing]
        );

        Assert.Equal([existing], resolved);
    }

    [Fact]
    public void ResolveInterceptorsWithoutAuthSchemesReturnsInterceptorsOnly()
    {
        var existing = new MarkerInterceptor("existing");

        var resolved = SmithyAuthSchemeResolver.ResolveInterceptors(
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
        var schemeA = new FakeScheme("scheme#a", new MarkerMiddleware("a"));
        var schemeB = new FakeScheme("scheme#b", new MarkerMiddleware("b"));

        var resolved = SmithyAuthSchemeResolver.Resolve(
            Endpoint,
            Service,
            ["scheme#a", "scheme#b"],
            authSchemes: [schemeB, schemeA]
        );

        Assert.Equal("a", Assert.IsType<MarkerMiddleware>(Assert.Single(resolved)).Tag);
    }

    [Fact]
    public void ResolveSkipsModeledSchemesWithoutAConfiguredMatch()
    {
        var schemeB = new FakeScheme("scheme#b", new MarkerMiddleware("b"));

        var resolved = SmithyAuthSchemeResolver.Resolve(
            Endpoint,
            Service,
            ["scheme#a", "scheme#b"],
            authSchemes: [schemeB]
        );

        Assert.Equal("b", Assert.IsType<MarkerMiddleware>(Assert.Single(resolved)).Tag);
    }

    [Fact]
    public void ResolveAppendsAuthAfterUserMiddleware()
    {
        var existing = new MarkerMiddleware("existing");
        var scheme = new FakeScheme("scheme#a", new MarkerMiddleware("a"));

        var resolved = SmithyAuthSchemeResolver.Resolve(
            Endpoint,
            Service,
            ["scheme#a"],
            authSchemes: [scheme],
            middleware: [existing]
        );

        Assert.Equal(2, resolved.Count);
        Assert.Equal("existing", Assert.IsType<MarkerMiddleware>(resolved[0]).Tag);
        Assert.Equal("a", Assert.IsType<MarkerMiddleware>(resolved[1]).Tag);
    }

    [Fact]
    public void ResolveInterceptorsAppendsAuthAfterUserInterceptors()
    {
        var existing = new MarkerInterceptor("existing");
        var auth = new MarkerInterceptor("auth");
        var scheme = new FakeInterceptorScheme("scheme#a", auth);

        var resolved = SmithyAuthSchemeResolver.ResolveInterceptors(
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
        var scheme = new FakeScheme("scheme#c", new MarkerMiddleware("c"));

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

    private sealed class FakeScheme(string schemeId, ISmithyClientMiddleware middleware)
        : ISmithyAuthScheme
    {
        public string SchemeId => schemeId;

        public ISmithyClientMiddleware CreateMiddleware(SmithyAuthSchemeContext context) =>
            middleware;
    }

    private sealed class FakeInterceptorScheme(string schemeId, IClientInterceptor interceptor)
        : ISmithyAuthScheme
    {
        public string SchemeId => schemeId;

        public ISmithyClientMiddleware CreateMiddleware(SmithyAuthSchemeContext context) =>
            new MarkerMiddleware("unused");

        public IClientInterceptor CreateInterceptor(SmithyAuthSchemeContext context) => interceptor;
    }

    private sealed class MarkerMiddleware(string tag) : ISmithyClientMiddleware
    {
        public string Tag => tag;

        public Task<SmithyOperationResponse> InvokeAsync(
            SmithyOperationRequest request,
            SmithyOperationNext nextOperation,
            CancellationToken cancellationToken = default
        ) => nextOperation(request, cancellationToken);
    }

    private sealed class MarkerInterceptor(string tag) : IClientInterceptor
    {
        public string Tag => tag;
    }
}
