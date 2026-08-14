namespace Bench.Clients;

/// <summary>
/// Records every request that passes through, then forwards it unchanged.
/// </summary>
/// <remarks>
/// Used for the parity gate, where each client runs against the shared reference
/// server: forwarding proves the client can actually round-trip against a real
/// server, and recording captures what it put on the wire. Never used while
/// benchmarking, the recording allocates.
/// </remarks>
public sealed class RecordingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly List<CapturedRequest> captures = [];

    public IReadOnlyList<CapturedRequest> Captures => captures;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        captures.Add(CapturedRequest.From(request, body));
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// The client-side scenario set: one call per operation, with inputs that mirror
/// the corresponding server corpus scenario.
/// </summary>
/// <remarks>
/// Mirroring matters. If the client suite exercised different inputs from the
/// server suite, the two halves would not be describing the same contract, and a
/// client request could not be checked against the server's expectations.
/// </remarks>
public static class BenchClientScenarios
{
    /// <summary>Matches <c>BenchCorpus.CreateOrderLarge</c>, which lands near 1 MB.</summary>
    private const int LargeOrderLines = 6_400;

    private const int SmallOrderLines = 5;

    public static BenchAddress Shipping { get; } =
        new("742 Evergreen Terrace", "Suite 100", "Springfield", "CA", "94043", "US");

    public static BenchAddress Billing { get; } =
        new("1600 Amphitheatre Parkway", "Suite 100", "Mountain View", "CA", "94043", "US");

    public static IReadOnlyDictionary<string, string> Metadata { get; } =
        new Dictionary<string, string>
        {
            ["source"] = "benchmark-suite",
            ["channel"] = "web",
            ["campaign"] = "none",
        };

    public static BenchSearchInput Search { get; } =
        new(
            Query: "benchmark",
            Category: "tools",
            MinPriceCents: 100,
            MaxPriceCents: 50_000,
            Sort: "priceAsc",
            Tags: ["featured", "sale"],
            RequestId: "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
            TenantId: "tenant-0007",
            CorrelationId: "9c858901-8a57-4791-81fe-4c455b099bc9",
            ClientVersion: "1.42.0"
        );

    public static BenchOrderInput SmallOrder { get; } = BuildOrder(SmallOrderLines);

    public static BenchOrderInput LargeOrder { get; } = BuildOrder(LargeOrderLines);

    /// <summary>Every client scenario, in a stable order.</summary>
    public static IReadOnlyList<BenchClientScenario> All { get; } =
    [
        new("client-get-item", async c => (await c.GetItemAsync("item-00042")).ToString()),
        new("client-list-items-1", async c => (await c.ListItemsAsync(1)).ToString()),
        new("client-list-items-100", async c => (await c.ListItemsAsync(100)).ToString()),
        new("client-list-items-10000", async c => (await c.ListItemsAsync(10_000)).ToString()),
        new("client-search-items", async c => (await c.SearchItemsAsync(Search)).ToString()),
        new(
            "client-create-order-small",
            async c => (await c.CreateOrderAsync(SmallOrder)).ToString()
        ),
        new(
            "client-create-order-large",
            async c => (await c.CreateOrderAsync(LargeOrder)).ToString()
        ),
    ];

    public static BenchClientScenario ByName(string name) =>
        All.FirstOrDefault(s => s.Name == name)
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown client scenario.");

    private static BenchOrderInput BuildOrder(int lineCount)
    {
        var lines = new BenchOrderLine[lineCount];
        for (var i = 0; i < lineCount; i++)
        {
            lines[i] = new BenchOrderLine(
                ItemId: $"item-{i % 10_000:D5}",
                Quantity: 1 + (i % 7),
                UnitPriceCents: 199 + (i * 37 % 90_000),
                Note: $"line {i}, gift wrap, leave at door",
                Attributes: new Dictionary<string, string>
                {
                    ["warehouse"] = $"wh-{i % 12:D2}",
                    ["lane"] = $"lane-{i % 40:D3}",
                }
            );
        }

        return new BenchOrderInput("cust-000042", lines, Shipping, Billing, Metadata);
    }
}

/// <summary>
/// One client scenario: a name, and the call to make. The invocation returns a
/// normalized string so the parity gate can assert every client parsed the
/// response into the same values.
/// </summary>
public sealed record BenchClientScenario(string Name, Func<IBenchClient, Task<string>> Invoke)
{
    public override string ToString() => Name;
}
