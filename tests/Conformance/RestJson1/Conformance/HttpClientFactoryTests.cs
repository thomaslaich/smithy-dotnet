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

    // Verifies the explicit-factory form used to pick a non-default protocol (or pass middleware /
    // an idempotency-token provider) in DI. This is the snippet documented for multi-protocol
    // services where AddHttpClient<I,T>'s default (primary protocol) is not what you want.
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
                static (http, _) => new RestJsonClient(http, protocol: new RestJson1Protocol())
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
