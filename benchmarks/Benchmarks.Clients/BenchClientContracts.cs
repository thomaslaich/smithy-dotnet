namespace Bench.Clients;

/// <summary>
/// The operations every client under measurement must expose, in terms that
/// belong to no particular client.
/// </summary>
/// <remarks>
/// Each generator produces a different API shape, so a thin adapter per client is
/// unavoidable. The rule for those adapters: they may translate, but they may not
/// work, anything a real caller leaves to the client stays inside the client, or
/// the benchmark measures the adapter. Results are normalized so the parity gate
/// can assert every client parsed the response into the same values.
/// </remarks>
public interface IBenchClient : IAsyncDisposable
{
    /// <summary>Stack name, used as a BenchmarkDotNet parameter and in parity output.</summary>
    string Name { get; }

    Task<BenchItemResult> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default
    );

    Task<BenchListResult> ListItemsAsync(int? count, CancellationToken cancellationToken = default);

    Task<BenchSearchResult> SearchItemsAsync(
        BenchSearchInput input,
        CancellationToken cancellationToken = default
    );

    Task<BenchOrderResult> CreateOrderAsync(
        BenchOrderInput input,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Normalized GetItem result.</summary>
public readonly record struct BenchItemResult(
    string ItemId,
    string Name,
    int PriceCents,
    bool InStock
);

/// <summary>
/// Normalized ListItems result. Only the count and a checksum of the ids are
/// kept: comparing full item lists across clients would be comparing the server's
/// response to itself, and the point is that each client parsed it identically.
/// </summary>
public readonly record struct BenchListResult(int Count, int IdChecksum);

/// <summary>Normalized SearchItems result, including the bound response header.</summary>
public readonly record struct BenchSearchResult(int Count, int TotalCount);

/// <summary>Normalized CreateOrder result.</summary>
public readonly record struct BenchOrderResult(string OrderId, long TotalCents, int LineCount);

/// <summary>Query and header inputs for SearchItems.</summary>
public sealed record BenchSearchInput(
    string? Query,
    string? Category,
    int? MinPriceCents,
    int? MaxPriceCents,
    string? Sort,
    IReadOnlyList<string>? Tags,
    string? RequestId,
    string? TenantId,
    string? CorrelationId,
    string? ClientVersion
);

/// <summary>Request body input for CreateOrder.</summary>
public sealed record BenchOrderInput(
    string CustomerId,
    IReadOnlyList<BenchOrderLine> Lines,
    BenchAddress? ShippingAddress,
    BenchAddress? BillingAddress,
    IReadOnlyDictionary<string, string>? Metadata
);

public sealed record BenchOrderLine(
    string ItemId,
    int Quantity,
    int UnitPriceCents,
    string? Note,
    IReadOnlyDictionary<string, string>? Attributes
);

public sealed record BenchAddress(
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string Country
);
