using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NSmithy.Client;
using NSmithy.Core;
using Nsmithy.Tests.Inventory;

namespace NSmithy.Tests.Client;

// Executable counterpart of the non-AWS routing example in the endpoint rules guide.
public sealed class InventoryEndpointExampleTests
{
    [Theory]
    [InlineData(false, "https://inventory.prod.example.test/stock/sku-1")]
    [InlineData(true, "https://inventory.local.example.test/stock/sku-1")]
    public async Task GeneratedRulesAndExplicitUrlsUseTheSameClient(
        bool explicitUrl,
        string expected
    )
    {
        var handler = new InventoryHandler();
        using var client = new InventoryClient(
            new HttpClient(handler),
            new InventoryClientConfig
            {
                Environment = "prod",
                Endpoint = explicitUrl ? new Uri("https://inventory.local.example.test") : null,
            }
        );
        var stock = await client.GetStockAsync(new GetStockInput("sku-1"));
        Assert.Equal(7, stock.Quantity);
        Assert.Equal(expected, handler.Uri!.AbsoluteUri);
    }

    [Fact]
    public async Task SharedDirectoryResolverCanBeInjectedWithoutRuleParameters()
    {
        var handler = new InventoryHandler();
        var services = new ServiceCollection();
        services.AddSingleton(
            new ServiceDirectory(
                new Dictionary<ShapeId, Uri>
                {
                    [ShapeId.Parse("nsmithy.tests.inventory#Inventory")] = new(
                        "https://inventory.directory.example.test"
                    ),
                }
            )
        );
        services.AddSingleton<DirectoryResolver>();
        services
            .AddInventoryClient(
                (provider, config) =>
                {
                    config.Environment = "unsupported"; // A replacement resolver bypasses these rules.
                    config.EndpointResolver = provider.GetRequiredService<DirectoryResolver>();
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IInventoryClient>();
        var stock = await client.GetStockAsync(new GetStockInput("sku-1"));
        Assert.Equal(7, stock.Quantity);
        Assert.Equal(
            "https://inventory.directory.example.test/stock/sku-1",
            handler.Uri!.AbsoluteUri
        );
    }

    private sealed class ServiceDirectory(IReadOnlyDictionary<ShapeId, Uri> endpoints)
    {
        public Uri GetEndpoint(ShapeId service) => endpoints[service];
    }

    private sealed class DirectoryResolver(ServiceDirectory directory) : IEndpointResolver
    {
        public ValueTask<SmithyEndpoint> ResolveEndpointAsync(
            SmithyEndpointParameters parameters,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new SmithyEndpoint(directory.GetEndpoint(parameters.ServiceId))
            );
        }
    }

    private sealed class InventoryHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Uri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"quantity\":7}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
