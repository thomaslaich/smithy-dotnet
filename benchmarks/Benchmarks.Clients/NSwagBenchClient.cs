using Bench.Stacks.NSwagGen;

namespace Bench.Clients;

/// <summary>The NSwag generated client, adapted to <see cref="IBenchClient"/>.</summary>
/// <remarks>
/// Generated from the same OpenAPI document NSmithy emits, so the contract is
/// shared by construction. Configured with <c>/JsonLibrary:SystemTextJson</c>
/// rather than left on the Newtonsoft.Json default, comparing Newtonsoft to
/// source-generated System.Text.Json would measure the JSON library rather than
/// the generator, and the point is to give each contender its best showing.
/// <para>
/// One difference from the baselines is inherent to what NSwag emits and stays in
/// the measurement: it uses reflection-based System.Text.Json rather than source
/// generation. That turns out not to cost it much, on the 10,000-item response
/// it allocates roughly half what the source-generated hand-written client does,
/// because it deserializes straight from the response stream rather than
/// buffering the body first.
/// </para>
/// </remarks>
public sealed class NSwagBenchClient : IBenchClient
{
    private readonly NSwagBenchmarkClient client;
    private readonly HttpClient httpClient;

    public NSwagBenchClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        client = new NSwagBenchmarkClient(
            httpClient.BaseAddress?.ToString() ?? "http://localhost/",
            httpClient
        );
    }

    public string Name => "nswag";

    public async Task<BenchItemResult> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        var output = await client.GetItemAsync(itemId, cancellationToken);
        return new BenchItemResult(output.ItemId, output.Name, output.PriceCents, output.InStock);
    }

    public async Task<BenchListResult> ListItemsAsync(
        int? count,
        CancellationToken cancellationToken = default
    )
    {
        var output = await client.ListItemsAsync(count, cancellationToken);
        // ICollection, not IList: the generated collection has no indexer.
        var checksum = 0;
        foreach (var item in output.Items)
            checksum = HashCode.Combine(checksum, item.ItemId);

        return new BenchListResult(output.Items.Count, checksum);
    }

    public async Task<BenchSearchResult> SearchItemsAsync(
        BenchSearchInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        var output = await client.SearchItemsAsync(
            input.Query,
            input.Category,
            input.MinPriceCents,
            input.MaxPriceCents,
            MapSort(input.Sort),
            input.Tags,
            input.ClientVersion,
            input.CorrelationId,
            input.RequestId,
            input.TenantId,
            cancellationToken
        );

        // The header is unreachable through the generated API; see NSwagResponseHeaders.
        return new BenchSearchResult(output.Items.Count, client.LastTotalCount);
    }

    public async Task<BenchOrderResult> CreateOrderAsync(
        BenchOrderInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = new List<OrderLine>(input.Lines.Count);
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            lines.Add(
                new OrderLine
                {
                    ItemId = line.ItemId,
                    Quantity = line.Quantity,
                    UnitPriceCents = line.UnitPriceCents,
                    Note = line.Note,
                    Attributes = ToStringMap(line.Attributes),
                }
            );
        }

        var output = await client.CreateOrderAsync(
            new CreateOrderRequestContent
            {
                CustomerId = input.CustomerId,
                Lines = lines,
                ShippingAddress = ToAddress(input.ShippingAddress),
                BillingAddress = ToAddress(input.BillingAddress),
                Metadata = ToStringMap(input.Metadata),
            },
            cancellationToken
        );

        return new BenchOrderResult(output.OrderId, output.TotalCents, output.LineCount);
    }

    /// <summary>NSwag models Smithy maps as a named subclass of Dictionary.</summary>
    private static StringMap? ToStringMap(IReadOnlyDictionary<string, string>? source)
    {
        if (source is null)
            return null;

        var map = new StringMap();
        foreach (var (key, value) in source)
            map[key] = value;

        return map;
    }

    // NSwag pascal-cases the Smithy enum's wire values into member names and
    // keeps the wire value on an EnumMember attribute.
    private static SortOrder? MapSort(string? wireValue) =>
        wireValue switch
        {
            null => null,
            "priceAsc" => SortOrder.PriceAsc,
            "priceDesc" => SortOrder.PriceDesc,
            "nameAsc" => SortOrder.NameAsc,
            "relevance" => SortOrder.Relevance,
            _ => throw new ArgumentOutOfRangeException(
                nameof(wireValue),
                wireValue,
                "Unknown sort."
            ),
        };

    private static Address? ToAddress(BenchAddress? address) =>
        address is null
            ? null
            : new Address
            {
                Line1 = address.Line1,
                Line2 = address.Line2,
                City = address.City,
                Region = address.Region,
                PostalCode = address.PostalCode,
                Country = address.Country,
            };

    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
