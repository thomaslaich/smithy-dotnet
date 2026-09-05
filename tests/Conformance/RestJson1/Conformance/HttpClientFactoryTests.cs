using Aws.Protocoltests.Restjson;
using Microsoft.Extensions.DependencyInjection;
using NSmithy.Protocols.RestJson;

namespace RestJson1.Conformance;

/// <summary>
/// Verifies the generated client integrates with IHttpClientFactory typed-client registration.
/// The generated client exposes several constructors; AddHttpClient must reliably select the
/// HttpClient-first one, and the factory-configured BaseAddress must flow through to the wire.
/// </summary>
public sealed class HttpClientFactoryTests
{
    [Fact]
    public void GeneratedClientResolvesAsTypedHttpClient()
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IRestJsonClient, RestJsonClient>(c =>
            c.BaseAddress = new Uri("https://example.test")
        );
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IRestJsonClient>();

        Assert.IsType<RestJsonClient>(client);
    }

    [Fact]
    public async Task TypedHttpClientUsesFactoryConfiguredBaseAddress()
    {
        var handler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.EmptyOk());
        var services = new ServiceCollection();
        services
            .AddHttpClient<IRestJsonClient, RestJsonClient>(c =>
                c.BaseAddress = new Uri("https://example.test")
            )
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IRestJsonClient>();

        await client.NoInputAndOutputAsync(new NoInputAndOutputInput(), CancellationToken.None);

        Assert.NotNull(handler.Captured);
        Assert.StartsWith("https://example.test/", handler.Captured!.RequestUri.ToString());
    }

    [Fact]
    public async Task DisposingGeneratedClientPreservesSuppliedHttpClientAndEndpointPrecedence()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            RecordingHttpMessageHandler.EmptyOk()
        );
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fallback.test") };
        var config = new RestJsonClientConfig { Endpoint = new Uri("https://configured.test") };
        using (var client = new RestJsonClient(http, config))
        {
            await client.NoInputAndOutputAsync(new NoInputAndOutputInput());
        }
        Assert.NotNull(handler.Captured);
        Assert.Equal("configured.test", handler.Captured.RequestUri.Host);
        Assert.Null(config.Protocol);
        Assert.Equal("fallback.test", http.BaseAddress.Host);
        using var response = await http.GetAsync("/");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void EndpointConstructorDoesNotMutateSharedConfig()
    {
        var config = new RestJsonClientConfig { Endpoint = new Uri("https://original.test") };
        using var client = new RestJsonClient(new Uri("https://override.test"), config);
        Assert.Equal("original.test", config.Endpoint.Host);
        Assert.Null(config.Protocol);
    }

    [Fact]
    public async Task FactoryConfigurationOverridesProtocolVersionDefaults()
    {
        Version? version = null;
        HttpVersionPolicy? policy = null;
        using var handler = new RecordingHttpMessageHandler(request =>
        {
            version = request.Version;
            policy = request.VersionPolicy;
            return RecordingHttpMessageHandler.EmptyOk();
        });
        var services = new ServiceCollection();
        services
            .AddRestJsonClient(http =>
            {
                http.BaseAddress = new Uri("https://example.test");
                http.DefaultRequestVersion = System.Net.HttpVersion.Version30;
                http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IRestJsonClient>();
        await client.NoInputAndOutputAsync(new NoInputAndOutputInput());
        Assert.Equal(System.Net.HttpVersion.Version30, version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrHigher, policy);
    }

    // Verifies the explicit-factory form used to pick a non-default protocol in DI. This is the
    // snippet documented for multi-protocol services where AddHttpClient<I,T>'s default (primary
    // protocol) is not what you want.
    [Fact]
    public async Task TypedHttpClientWithExplicitProtocolFactory()
    {
        var handler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.EmptyOk());
        var services = new ServiceCollection();
        services
            .AddHttpClient(
                nameof(RestJsonClient),
                c => c.BaseAddress = new Uri("https://example.test")
            )
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddTypedClient<IRestJsonClient>(
                static (http, _) =>
                    new RestJsonClient(http, new() { Protocol = new RestJson1Protocol() })
            );
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IRestJsonClient>();

        await client.NoInputAndOutputAsync(new NoInputAndOutputInput(), CancellationToken.None);

        Assert.NotNull(handler.Captured);
        Assert.StartsWith("https://example.test/", handler.Captured!.RequestUri.ToString());
    }

    // The generated opt-in extension (SmithyGenerateDependencyInjection=true) — a turnkey
    // AddRestJsonClient(endpoint) registration.
    [Fact]
    public async Task GeneratedAddClientExtensionRegistersTypedClient()
    {
        var handler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.EmptyOk());
        var services = new ServiceCollection();
        services
            .AddRestJsonClient(new Uri("https://example.test"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IRestJsonClient>();

        await client.NoInputAndOutputAsync(new NoInputAndOutputInput(), CancellationToken.None);

        Assert.NotNull(handler.Captured);
        Assert.StartsWith("https://example.test/", handler.Captured!.RequestUri.ToString());
    }
}
