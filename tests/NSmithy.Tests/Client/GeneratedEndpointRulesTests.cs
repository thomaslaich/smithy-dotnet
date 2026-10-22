using System.Net;
using NSmithy.Client;
using NSmithy.Core;
using Nsmithy.Tests.Endpoints;

namespace NSmithy.Tests.Client;

// Generated client for model/endpoints.smithy, whose endpoints come from its rules.
public sealed class GeneratedEndpointRulesTests
{
    [Fact]
    public async Task ClientResolvesTheRegionalEndpointFromConfiguration()
    {
        var handler = new RecordingHandler();
        using var client = new StorageClient(
            new HttpClient(handler),
            new StorageClientConfig { Region = "eu-west-1" }
        );

        await client.ListBucketsAsync(new ListBucketsInput());

        Assert.Equal(
            new Uri("https://storage.eu-west-1.example.com/"),
            handler.Request!.RequestUri
        );
    }

    [Fact]
    public async Task ConfigConstructorNeedsNoEndpoint()
    {
        using var client = new StorageClient(
            new StorageClientConfig { Region = "eu-west-1", UseFips = true }
        );

        var resolver = new StorageEndpointResolver(
            new StorageClientConfig { Region = "eu-west-1", UseFips = true }
        );
        var endpoint = await resolver.ResolveEndpointAsync(Parameters("ListBuckets", null));

        Assert.Equal(new Uri("https://storage-fips.eu-west-1.example.com"), endpoint.Uri);
    }

    [Fact]
    public async Task ConfiguredEndpointIsTheRulesCustomEndpoint()
    {
        var handler = new RecordingHandler();
        using var client = new StorageClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9000") },
            new StorageClientConfig { Region = "eu-west-1" }
        );

        await client.ListBucketsAsync(new ListBucketsInput());

        Assert.Equal(new Uri("http://localhost:9000/"), handler.Request!.RequestUri);
    }

    [Fact]
    public async Task RuleErrorsSurfaceAsResolutionExceptions()
    {
        using var client = new StorageClient(
            new HttpClient(new RecordingHandler()) { BaseAddress = new Uri("http://localhost") },
            new StorageClientConfig { UseFips = true }
        );

        var error = await Assert.ThrowsAsync<SmithyEndpointResolutionException>(() =>
            client.ListBucketsAsync(new ListBucketsInput())
        );
        Assert.Equal("FIPS and custom endpoint are not supported", error.Message);
    }

    [Theory]
    [InlineData(null, "http://localhost:9000")]
    [InlineData("https://explicit.example.com", "https://explicit.example.com")]
    public async Task ResolverUsesConfiguredEndpointUnlessExplicitlyOverridden(
        string? explicitEndpoint,
        string expected
    )
    {
        var resolver = new StorageEndpointResolver(
            new StorageClientConfig { Endpoint = new Uri("http://localhost:9000") },
            explicitEndpoint is null ? null : new Uri(explicitEndpoint)
        );

        var endpoint = await resolver.ResolveEndpointAsync(Parameters("ListBuckets", null));

        Assert.Equal(new Uri(expected), endpoint.Uri);
    }

    [Fact]
    public async Task ContextParamsBindFromTheOperationInput()
    {
        var handler = new RecordingHandler();
        using var client = new StorageClient(
            new HttpClient(handler),
            new StorageClientConfig { Region = "eu-west-1" }
        );

        await client.GetObjectAsync(new GetObjectInput("photo.jpg", Bucket: "holiday"));

        Assert.Equal(
            new Uri(
                "https://holiday.storage.eu-west-1.example.com/objects/photo.jpg?bucket=holiday"
            ),
            handler.Request!.RequestUri
        );
        Assert.Equal(["holiday"], handler.Request.Headers.GetValues("x-bucket"));
    }

    [Fact]
    public async Task CustomResolverReplacesRulesWithoutRequiringRuleParameters()
    {
        var handler = new RecordingHandler();
        using var client = new StorageClient(
            new HttpClient(handler),
            new StorageClientConfig
            {
                Endpoint = new Uri("https://ignored.example.test"),
                UseFips = true,
                EndpointResolver = new StaticEndpointResolver(
                    new Uri("https://custom.example.test")
                ),
            }
        );
        await client.ListBucketsAsync(new ListBucketsInput());
        Assert.Equal(new Uri("https://custom.example.test/"), handler.Request!.RequestUri);
    }

    [Fact]
    public async Task EndpointCarriesAuthSchemeProperties()
    {
        var resolver = new StorageEndpointResolver(
            new StorageClientConfig { Region = "eu-west-1" }
        );

        var endpoint = await resolver.ResolveEndpointAsync(
            Parameters("GetObject", new GetObjectInput("photo.jpg", Bucket: "holiday"))
        );

        var scheme = Assert.Single(endpoint.AuthSchemes!);
        Assert.Equal("aws.auth#sigv4", scheme.SchemeId);
        Assert.Equal("storage", scheme.GetString("signingName"));
        Assert.Equal("eu-west-1", scheme.GetString("signingRegion"));
    }

    [Fact]
    public void ClientLevelParametersResolveOnceAtConstruction()
    {
        var resolver = new StorageEndpointResolver(
            new StorageEndpointParameters { Region = "us-east-1" }
        );

        // GetObject binds Bucket per call, so nothing is fixed up front.
        Assert.Null(resolver.StaticEndpoint);
        Assert.Equal(
            new Uri("https://storage.us-east-1.example.com"),
            StorageEndpointResolver
                .Resolve(new StorageEndpointParameters { Region = "us-east-1" })
                .Uri
        );
    }

    [Fact]
    public void ConfigCopyKeepsEndpointParameters()
    {
        var copy = new StorageClientConfig(
            new StorageClientConfig { Region = "eu-west-1", UseFips = true }
        );

        Assert.Equal("eu-west-1", copy.Region);
        Assert.True(copy.UseFips);
    }

    [Fact]
    public void MissingRegionIsARuleError()
    {
        var error = Assert.Throws<SmithyEndpointResolutionException>(() =>
            StorageEndpointResolver.Resolve(new StorageEndpointParameters())
        );
        Assert.Equal("Region must be set", error.Message);
    }

    private static SmithyEndpointParameters Parameters(string operation, object? input) =>
        new(
            ShapeId.Parse("nsmithy.tests.endpoints#Storage"),
            ShapeId.Parse($"nsmithy.tests.endpoints#{operation}"),
            null,
            input
        );

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
