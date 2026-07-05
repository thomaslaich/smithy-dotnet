using NSmithy.Client;

namespace NSmithy.Tests.Client;

public sealed class SmithyClientConfigTests
{
    [Fact]
    public void CopyConstructorCopiesAllCommonOptions()
    {
        var interceptor = new NoopInterceptor();
        var strategy = new SmithySimpleRetryStrategy();
        var authScheme = new HttpBearerAuthScheme("token");
        var endpointResolver = new StaticEndpointResolver(new Uri("https://api.example.com"));
        Func<string> tokenProvider = static () => "token-1";
        var source = new TestConfig
        {
            Endpoint = new Uri("https://api.example.com"),
            EndpointResolver = endpointResolver,
            RetryStrategy = strategy,
            OperationTimeout = TimeSpan.FromSeconds(10),
            IdempotencyTokenProvider = tokenProvider,
            Interceptors = { interceptor },
            AuthSchemes = { authScheme },
        };

        var copy = new TestConfig(source);

        Assert.Equal(source.Endpoint, copy.Endpoint);
        Assert.Same(endpointResolver, copy.EndpointResolver);
        Assert.Equal(TimeSpan.FromSeconds(10), copy.OperationTimeout);
        // Shallow on purpose: shared strategy instances share client-wide state (retry quota).
        Assert.Same(strategy, copy.RetryStrategy);
        Assert.Same(tokenProvider, copy.IdempotencyTokenProvider);
        Assert.Equal([interceptor], copy.Interceptors);
        Assert.Equal([authScheme], copy.AuthSchemes);
    }

    [Fact]
    public void MutatingTheCopyDoesNotAffectTheSource()
    {
        var source = new TestConfig { Endpoint = new Uri("https://one.example.com") };

        var copy = new TestConfig(source) { Endpoint = new Uri("https://two.example.com") };
        copy.Interceptors.Add(new NoopInterceptor());

        Assert.Equal(new Uri("https://one.example.com"), source.Endpoint);
        Assert.Empty(source.Interceptors);
    }

    private sealed class TestConfig : SmithyClientConfig
    {
        public TestConfig() { }

        public TestConfig(TestConfig source)
            : base(source) { }
    }

    private sealed class NoopInterceptor : IClientInterceptor;
}
