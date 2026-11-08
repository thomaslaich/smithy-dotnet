using System.Globalization;
using Bench.Domain;
using NSmithyBenchmarkService;
using TypeSpec.Helpers;

namespace Bench.Stacks.TypeSpec;

/// <summary>
/// The TypeSpec stack's handler: the generated operations interface implemented
/// against <see cref="BenchDomain"/>, mapping domain records into the generated
/// models like every other stack does.
/// </summary>
public sealed class TypeSpecBenchmarkOperations : InSmithyBenchmarkServiceOperations
{
    public Task<ListItemsResponseContent> ListItemsAsync(int? count) =>
        Task.FromResult(
            new ListItemsResponseContent { Items = ToSummaries(BenchDomain.ListItems(count)) }
        );

    public Task<GetItemResponseContent> GetItemAsync(string itemId)
    {
        var item =
            BenchDomain.GetItem(itemId)
            ?? throw new HttpServiceException(
                StatusCodes.Status404NotFound,
                new Model0 { ItemId = itemId, Message = $"No item with id '{itemId}'." },
                new Dictionary<string, string> { ["X-Amzn-Errortype"] = "ItemNotFound" }
            );

        return Task.FromResult(
            new GetItemResponseContent
            {
                ItemId = item.ItemId,
                Name = item.Name,
                PriceCents = item.PriceCents,
                InStock = item.InStock,
            }
        );
    }

    public Task<NSmithyBenchmarkServiceOperationsCreateOrderResponse> CreateOrderAsync(
        CreateOrderRequestContent body
    )
    {
        var lines = body.Lines;
        var domainLines = new OrderLineInput[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            domainLines[i] = new OrderLineInput(line.ItemId, line.Quantity, line.UnitPriceCents);
        }

        var result = BenchDomain.CreateOrder(body.CustomerId, domainLines);
        return Task.FromResult(
            new NSmithyBenchmarkServiceOperationsCreateOrderResponse
            {
                OrderId = result.OrderId,
                TotalCents = result.TotalCents,
                LineCount = result.LineCount,
            }
        );
    }

    public Task<NSmithyBenchmarkServiceOperationsSearchItemsResponse> SearchItemsAsync(
        string? q,
        string? category,
        int? minPriceCents,
        int? maxPriceCents,
        SortOrder? sort,
        string[]? tags,
        string? xClientVersion,
        string? xCorrelationId,
        string? xRequestId,
        string? xTenantId
    )
    {
        var sortName = sort switch
        {
            SortOrder.PriceAsc => "priceAsc",
            SortOrder.PriceDesc => "priceDesc",
            SortOrder.NameAsc => "nameAsc",
            SortOrder.Relevance => "relevance",
            _ => null,
        };

        var result = BenchDomain.Search(q, category, minPriceCents, maxPriceCents, sortName, tags);
        return Task.FromResult(
            new NSmithyBenchmarkServiceOperationsSearchItemsResponse
            {
                XTotalCount = result.TotalCount,
                Body = new SearchItemsResponseContent { Items = ToSummaries(result.Items) },
            }
        );
    }

    private static ItemSummary[] ToSummaries(ArraySegment<BenchItem> items)
    {
        var summaries = new ItemSummary[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            summaries[i] = new ItemSummary
            {
                ItemId = item.ItemId,
                Name = item.Name,
                PriceCents = item.PriceCents,
                InStock = item.InStock,
                Category = item.Category!,
                Tags = item.Tags is string[] array ? array : [.. item.Tags ?? []],
            };
        }

        return summaries;
    }
}
