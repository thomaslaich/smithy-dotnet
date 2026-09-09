using System.Net;
using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.RestJson;

namespace NSmithy.Tests.Client;

public sealed class SmithyHttpClientEnvironmentTests
{
    private static readonly ServiceSchema Service = Schemas.Service(
        new ShapeId("example", "Service")
    );

    [Fact]
    public async Task DisposingOwnedEnvironmentClosesItsTransport()
    {
        var environment = SmithyHttpClientEnvironment.Create(
            Service,
            new() { Endpoint = new Uri("https://example.test") },
            static () => new RestJson1Protocol(),
            []
        );
        environment.Dispose();
        environment.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            environment.Runtime.InvokeAsync(
                new SmithyOperationBinding<string, string>(
                    Service.Id,
                    new ShapeId("example", "Read"),
                    new EmptyProtocol()
                ),
                "input"
            )
        );
    }

    [Fact]
    public async Task SuppliedHttpClientSurvivesDisposalAndKeepsItsSettings()
    {
        using var http = new HttpClient(new OkHandler())
        {
            BaseAddress = new Uri("https://example.test"),
            DefaultRequestVersion = HttpVersion.Version30,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        var config = new SmithyClientConfig();
        var environment = SmithyHttpClientEnvironment.Create(
            Service,
            config,
            static () => new RestJson1Protocol(),
            [],
            SmithyHttpVersionPreference.Http2,
            http
        );
        environment.Dispose();

        using var response = await http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version30, http.DefaultRequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrHigher, http.DefaultVersionPolicy);
        Assert.Null(config.Endpoint);
        Assert.Null(config.Protocol);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void ExplicitProtocolOverridesModeledVersion(bool explicitProtocol, int expectedMajor)
    {
        using var http = new HttpClient();
        SmithyHttpClientEnvironment.ConfigureHttpClient(
            http,
            new() { Protocol = explicitProtocol ? new RestJson1Protocol() : null },
            static () => new RestJson1Protocol(),
            SmithyHttpVersionPreference.Http2
        );
        Assert.Equal(expectedMajor, http.DefaultRequestVersion.Major);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, http.DefaultVersionPolicy);
    }

    [Fact]
    public async Task SuppliedRuntimeSurvivesDisposalAndIgnoresConstructionOptions()
    {
        using var http = new HttpClient(new OkHandler());
        var runtime = new SmithyClientRuntime(
            new HttpClientTransport(http),
            endpoint: new Uri("https://example.test")
        );
        var environment = SmithyHttpClientEnvironment.FromRuntime(
            Service,
            runtime,
            new() { OperationTimeout = TimeSpan.FromTicks(-1) },
            static () => new RestJson1Protocol()
        );
        environment.Dispose();
        Assert.Same(runtime, environment.Runtime);
        Assert.Equal(
            "output",
            await runtime.InvokeAsync(
                new SmithyOperationBinding<string, string>(
                    Service.Id,
                    new ShapeId("example", "Read"),
                    new EmptyProtocol()
                ),
                "input"
            )
        );
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }
            );
    }

    private sealed class EmptyProtocol : IClientOperationProtocol<string, string>
    {
        public SmithyHttpRequest SerializeRequest(
            string input,
            CancellationToken cancellationToken = default
        ) => new(HttpMethod.Get, "/");

        public ValueTask<string> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult("output");

        public bool IsErrorResponse(SmithyHttpClientResponse response) => false;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<Exception?>(null);
    }
}
