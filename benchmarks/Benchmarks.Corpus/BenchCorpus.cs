using System.Text;
using System.Text.Json;

namespace Bench.Corpus;

/// <summary>
/// The request corpus every stack is measured against, and the parity gate
/// compares over.
/// </summary>
public static class BenchCorpus
{
    /// <summary>
    /// Order line count that lands the CreateOrder body near 1 MB. Each line
    /// serializes to roughly 164 bytes; re-measure if the line shape changes.
    /// </summary>
    private const int LargeOrderLines = 6_400;

    private const int SmallOrderLines = 5;

    public static BenchRequest GetItemHit { get; } =
        new("get-item-hit", "GET", "/items/item-00042", [], null);

    public static BenchRequest GetItemMiss { get; } =
        new("get-item-miss", "GET", "/items/item-99999", [], null);

    public static BenchRequest SearchItems { get; } =
        new(
            "search-items",
            "GET",
            "/search/items?q=benchmark&category=tools&minPriceCents=100"
                + "&maxPriceCents=50000&sort=priceAsc&tags=featured&tags=sale",
            [
                ("x-request-id", "3f2504e0-4f89-11d3-9a0c-0305e82c3301"),
                ("x-tenant-id", "tenant-0007"),
                ("x-correlation-id", "9c858901-8a57-4791-81fe-4c455b099bc9"),
                ("x-client-version", "1.42.0"),
            ],
            null
        );

    public static BenchRequest ListItems1 { get; } =
        new("list-items-1", "GET", "/items?count=1", [], null);

    public static BenchRequest ListItems100 { get; } =
        new("list-items-100", "GET", "/items?count=100", [], null);

    public static BenchRequest ListItems10000 { get; } =
        new("list-items-10000", "GET", "/items?count=10000", [], null);

    /// <summary>
    /// Scenario: the validation path, via a violated <c>@range</c> on a query
    /// parameter (<c>count</c> is capped at 10000).
    /// </summary>
    public static BenchRequest ValidationRange { get; } =
        new("validation-range", "GET", "/items?count=99999", [], null);

    /// <summary>
    /// Scenario: the validation path, via a violated <c>@pattern</c> on a path
    /// label. Distinct from <see cref="GetItemMiss"/>, which is a well-formed id
    /// that simply is not in the catalog and so returns 404, not 400.
    /// </summary>
    public static BenchRequest ValidationPattern { get; } =
        new("validation-pattern", "GET", "/items/not-an-item", [], null);

    /// <summary>
    /// Scenario: two constraint violations in one request, which exercises the
    /// aggregation path rather than the single-failure shortcut.
    /// </summary>
    public static BenchRequest ValidationMulti { get; } =
        new("validation-multi", "GET", "/search/items?minPriceCents=-1&maxPriceCents=-2", [], null);

    /// <summary>
    /// Scenario: the validation path via a violated <c>@length</c>, and the only
    /// validation scenario that fails on a request body rather than on a query
    /// parameter or path label.
    /// </summary>
    public static BenchRequest ValidationLength { get; } =
        new(
            "validation-length",
            "POST",
            "/orders",
            [("content-type", "application/json")],
            BuildOrderBody(SmallOrderLines, customerId: "")
        );

    public static BenchRequest CreateOrderSmall { get; } =
        new(
            "create-order-small",
            "POST",
            "/orders",
            [("content-type", "application/json")],
            BuildOrderBody(SmallOrderLines)
        );

    public static BenchRequest CreateOrderLarge { get; } =
        new(
            "create-order-large",
            "POST",
            "/orders",
            [("content-type", "application/json")],
            BuildOrderBody(LargeOrderLines)
        );

    /// <summary>Every scenario, in a stable order.</summary>
    public static IReadOnlyList<BenchRequest> All { get; } =
    [
        GetItemHit,
        GetItemMiss,
        SearchItems,
        ListItems1,
        ListItems100,
        ListItems10000,
        CreateOrderSmall,
        CreateOrderLarge,
        ValidationRange,
        ValidationPattern,
        ValidationMulti,
        ValidationLength,
    ];

    /// <summary>Looks a scenario up by its <see cref="BenchRequest.Name"/>.</summary>
    public static BenchRequest ByName(string name) =>
        All.FirstOrDefault(r => r.Name == name)
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown corpus scenario.");

    private static byte[] BuildOrderBody(int lineCount, string customerId = "cust-000042")
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("customerId", customerId);

            writer.WritePropertyName("lines");
            writer.WriteStartArray();
            for (var i = 0; i < lineCount; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("itemId", $"item-{i % 10_000:D5}");
                writer.WriteNumber("quantity", 1 + (i % 7));
                writer.WriteNumber("unitPriceCents", 199 + (i * 37 % 90_000));
                writer.WriteString("note", $"line {i} \u2014 gift wrap, leave at door");
                writer.WritePropertyName("attributes");
                writer.WriteStartObject();
                writer.WriteString("warehouse", $"wh-{i % 12:D2}");
                writer.WriteString("lane", $"lane-{i % 40:D3}");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            WriteAddress(writer, "shippingAddress", "742 Evergreen Terrace", "Springfield");
            WriteAddress(writer, "billingAddress", "1600 Amphitheatre Parkway", "Mountain View");

            writer.WritePropertyName("metadata");
            writer.WriteStartObject();
            writer.WriteString("source", "benchmark-suite");
            writer.WriteString("channel", "web");
            writer.WriteString("campaign", "none");
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteAddress(
        Utf8JsonWriter writer,
        string propertyName,
        string line1,
        string city
    )
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("line1", line1);
        writer.WriteString("line2", "Suite 100");
        writer.WriteString("city", city);
        writer.WriteString("region", "CA");
        writer.WriteString("postalCode", "94043");
        writer.WriteString("country", "US");
        writer.WriteEndObject();
    }
}

/// <summary>One scenario: a complete HTTP request, independent of any stack.</summary>
/// <param name="Name">Stable scenario id, used as the BenchmarkDotNet parameter and parity key.</param>
/// <param name="Method">HTTP method.</param>
/// <param name="PathAndQuery">Absolute path plus query string.</param>
/// <param name="Headers">Request headers beyond those the transport sets itself.</param>
/// <param name="Body">Request body bytes, or null for bodyless requests.</param>
public sealed record BenchRequest(
    string Name,
    string Method,
    string PathAndQuery,
    IReadOnlyList<(string Name, string Value)> Headers,
    byte[]? Body
)
{
    /// <summary>Body size in bytes, for reporting alongside throughput numbers.</summary>
    public int BodyBytes => Body?.Length ?? 0;

    public override string ToString() => Name;

    /// <summary>The body as UTF-8 text. Diagnostics and parity failure messages only.</summary>
    public string? BodyText => Body is null ? null : Encoding.UTF8.GetString(Body);
}
