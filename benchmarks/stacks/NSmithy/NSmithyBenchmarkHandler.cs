using Bench.Domain;
using Nsmithy.Bench;

// Namespace is deliberately flat and free of an "NSmithy" segment: a namespace
// containing one would shadow the runtime's own NSmithy.* namespaces for every
// file in this assembly.
namespace Bench.Stacks;

/// <summary>
/// The NSmithy stack's implementation of the benchmark contract.
/// </summary>
/// <remarks>
/// Every method does two things and nothing else: call <see cref="BenchDomain"/>,
/// then map the result into the generated wire types. That mapping is on purpose
///, it is the work a real user does, and keeping it inside the measurement is
/// what makes the comparison against the hand-written baselines honest.
/// </remarks>
public sealed class NSmithyBenchmarkHandler : IBenchmarkServiceHandler
{
    public Task<GetItemOutput> GetItemAsync(
        GetItemInput input,
        CancellationToken cancellationToken = default
    )
    {
        var item = BenchDomain.GetItem(input.ItemId);
        if (item is null)
            throw new ItemNotFound($"No item with id '{input.ItemId}'.", input.ItemId);

        return Task.FromResult(
            new GetItemOutput(
                InStock: item.InStock,
                ItemId: item.ItemId,
                Name: item.Name,
                PriceCents: item.PriceCents
            )
        );
    }

    public Task<SearchItemsOutput> SearchItemsAsync(
        SearchItemsInput input,
        CancellationToken cancellationToken = default
    )
    {
        var result = BenchDomain.Search(
            input.Query,
            input.Category,
            input.MinPriceCents,
            input.MaxPriceCents,
            input.Sort?.Value,
            input.Tags?.Values
        );

        return Task.FromResult(new SearchItemsOutput(ToSummaries(result.Items), result.TotalCount));
    }

    public Task<ListItemsOutput> ListItemsAsync(
        ListItemsInput input,
        CancellationToken cancellationToken = default
    )
    {
        var items = BenchDomain.ListItems(input.Count);
        return Task.FromResult(new ListItemsOutput(ToSummaries(items)));
    }

    public Task<CreateOrderOutput> CreateOrderAsync(
        CreateOrderInput input,
        CancellationToken cancellationToken = default
    )
    {
        var lines = input.Lines.Values;
        var domainLines = new OrderLineInput[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            domainLines[i] = new OrderLineInput(line.ItemId, line.Quantity, line.UnitPriceCents);
        }

        var result = BenchDomain.CreateOrder(input.CustomerId, domainLines);
        return Task.FromResult(
            new CreateOrderOutput(result.LineCount, result.OrderId, result.TotalCents)
        );
    }

    private static ItemSummaries ToSummaries(ArraySegment<BenchItem> items)
    {
        var summaries = new ItemSummary[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            summaries[i] = new ItemSummary(
                InStock: item.InStock,
                ItemId: item.ItemId,
                Name: item.Name,
                PriceCents: item.PriceCents,
                Category: item.Category,
                // NSmithy models lists as nominal wrapper types that defensively
                // copy on construction. That extra allocation per list is a real
                // cost the System.Text.Json baselines do not pay; it stays in the
                // measurement rather than being worked around here.
                Tags: item.Tags is null ? null : new StringList(item.Tags)
            );
        }

        return new ItemSummaries(summaries);
    }
}
