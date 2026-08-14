namespace Bench.Domain;

/// <summary>
/// The business logic every stack in the suite calls into.
/// </summary>
public static class BenchDomain
{
    /// <summary>Largest list the corpus asks for; the catalog is sized to match.</summary>
    public const int CatalogSize = 10_000;

    // Declared before Catalog on purpose: static field initializers run in
    // textual order, and BuildCatalog reads this.
    private static readonly string[] Categories =
    [
        "tools",
        "hardware",
        "consumables",
        "electronics",
    ];

    private static readonly BenchItem[] Catalog = BuildCatalog();

    private static BenchItem[] BuildCatalog()
    {
        var items = new BenchItem[CatalogSize];
        for (var i = 0; i < items.Length; i++)
        {
            // Deterministic, and varied enough that JSON escaping and number
            // widths are not uniformly the cheapest case.
            items[i] = new BenchItem(
                ItemId: $"item-{i:D5}",
                Name: $"Benchmark Item {i} \u2014 {Categories[i % Categories.Length]}",
                PriceCents: 199 + (i * 37 % 90_000),
                InStock: i % 3 != 0,
                Category: Categories[i % Categories.Length],
                Tags: i % 4 == 0 ? ["featured", "sale"] : ["standard"]
            );
        }

        return items;
    }

    /// <summary>Scenario: minimal work. Returns null when the id is not in the catalog.</summary>
    public static BenchItem? GetItem(string itemId)
    {
        var index = ParseItemIndex(itemId);
        return index is >= 0 and < CatalogSize ? Catalog[index] : null;
    }

    /// <summary>
    /// Scenario: HTTP binding cost. The filtering is intentionally cheap, the
    /// operation exists to move query and header values, not to exercise search.
    /// </summary>
    public static SearchResult Search(
        string? query,
        string? category,
        int? minPriceCents,
        int? maxPriceCents,
        string? sort,
        IReadOnlyList<string>? tags
    )
    {
        // A fixed-size window keeps the body small so the numbers reflect
        // query/header handling rather than payload serialization.
        var categoryOffset = category is null ? 0 : Array.IndexOf(Categories, category) + 1;
        var start = Math.Abs(categoryOffset * 37 + (query?.Length ?? 0)) % (CatalogSize - 16);
        var window = new ArraySegment<BenchItem>(Catalog, start, 8);

        var total = CatalogSize;
        if (minPriceCents is not null || maxPriceCents is not null)
            total /= 2;
        if (tags is { Count: > 0 })
            total -= tags.Count;

        return new SearchResult(window, total);
    }

    /// <summary>
    /// Scenario: response body scaling. Returns a slice of the prebuilt catalog,
    /// so the cost of this call does not grow with <paramref name="count"/> ,
    /// only the serialization downstream of it does.
    /// </summary>
    public static ArraySegment<BenchItem> ListItems(int? count)
    {
        var n = Math.Clamp(count ?? 100, 0, CatalogSize);
        return new ArraySegment<BenchItem>(Catalog, 0, n);
    }

    /// <summary>
    /// Scenario: large nested request body. The work is proportional to the
    /// number of lines, which is the point, a stack that deserialized lazily
    /// would still have to materialize every line to get the right total.
    /// </summary>
    public static OrderResult CreateOrder(string customerId, IReadOnlyList<OrderLineInput> lines)
    {
        long total = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            total += (long)line.Quantity * line.UnitPriceCents;
        }

        // Deterministic id: the suite compares responses across stacks byte for
        // byte, so nothing here may vary between runs or between stacks.
        return new OrderResult(
            OrderId: $"order-{customerId}-{lines.Count:D4}",
            TotalCents: total,
            LineCount: lines.Count
        );
    }

    private static int ParseItemIndex(string itemId) =>
        itemId.StartsWith("item-", StringComparison.Ordinal)
        && int.TryParse(itemId.AsSpan(5), out var index)
            ? index
            : -1;
}

/// <summary>An item as the domain knows it, independent of any stack's wire types.</summary>
public sealed record BenchItem(
    string ItemId,
    string Name,
    int PriceCents,
    bool InStock,
    string? Category,
    IReadOnlyList<string>? Tags
);

/// <summary>A search hit window plus the total the caller would page through.</summary>
public readonly record struct SearchResult(ArraySegment<BenchItem> Items, int TotalCount);

/// <summary>The subset of an order line the domain needs to price an order.</summary>
public readonly record struct OrderLineInput(string ItemId, int Quantity, int UnitPriceCents);

/// <summary>The priced result of an order.</summary>
public readonly record struct OrderResult(string OrderId, long TotalCents, int LineCount);
