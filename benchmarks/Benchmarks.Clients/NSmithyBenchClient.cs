using Nsmithy.Bench;

namespace Bench.Clients;

/// <summary>The NSmithy generated client, adapted to <see cref="IBenchClient"/>.</summary>
/// <remarks>
/// Configuration is left at defaults on purpose. The generated client carries
/// retry and telemetry machinery the other clients do not have, and that is a
/// real cost of using it. With no <c>ActivityListener</c> registered the
/// telemetry is close to free, but it is disclosed rather than disabled, tuning
/// one client's configuration to win is exactly what this suite exists to
/// prevent.
/// </remarks>
public sealed class NSmithyBenchClient : IBenchClient
{
    private readonly BenchmarkServiceClient client;
    private readonly HttpClient httpClient;

    public NSmithyBenchClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
        client = new BenchmarkServiceClient(httpClient);
    }

    public string Name => "nsmithy";

    public async Task<BenchItemResult> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        var output = await client.GetItemAsync(new GetItemInput(itemId), cancellationToken);
        return new BenchItemResult(output.ItemId, output.Name, output.PriceCents, output.InStock);
    }

    public async Task<BenchListResult> ListItemsAsync(
        int? count,
        CancellationToken cancellationToken = default
    )
    {
        var output = await client.ListItemsAsync(new ListItemsInput(count), cancellationToken);
        var items = output.Items.Values;
        var checksum = 0;
        for (var i = 0; i < items.Count; i++)
            checksum = HashCode.Combine(checksum, items[i].ItemId);

        return new BenchListResult(items.Count, checksum);
    }

    public async Task<BenchSearchResult> SearchItemsAsync(
        BenchSearchInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        var output = await client.SearchItemsAsync(
            new SearchItemsInput(
                Category: input.Category,
                ClientVersion: input.ClientVersion,
                CorrelationId: input.CorrelationId,
                MaxPriceCents: input.MaxPriceCents,
                MinPriceCents: input.MinPriceCents,
                Query: input.Query,
                RequestId: input.RequestId,
                Sort: input.Sort is null ? null : SortOrder.FromValue(input.Sort),
                Tags: input.Tags is null ? null : new StringList(input.Tags),
                TenantId: input.TenantId
            ),
            cancellationToken
        );

        return new BenchSearchResult(output.Items.Values.Count, output.TotalCount);
    }

    public async Task<BenchOrderResult> CreateOrderAsync(
        BenchOrderInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = new OrderLine[input.Lines.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = input.Lines[i];
            lines[i] = new OrderLine(
                ItemId: line.ItemId,
                Quantity: line.Quantity,
                UnitPriceCents: line.UnitPriceCents,
                Attributes: line.Attributes is null ? null : new StringMap(line.Attributes),
                Note: line.Note
            );
        }

        var output = await client.CreateOrderAsync(
            new CreateOrderInput(
                CustomerId: input.CustomerId,
                Lines: new OrderLines(lines),
                BillingAddress: ToAddress(input.BillingAddress),
                Metadata: input.Metadata is null ? null : new StringMap(input.Metadata),
                ShippingAddress: ToAddress(input.ShippingAddress)
            ),
            cancellationToken
        );

        return new BenchOrderResult(output.OrderId, output.TotalCents, output.LineCount);
    }

    private static Address? ToAddress(BenchAddress? address) =>
        address is null
            ? null
            : new Address(
                City: address.City,
                Country: address.Country,
                Line1: address.Line1,
                PostalCode: address.PostalCode,
                Line2: address.Line2,
                Region: address.Region
            );

    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
