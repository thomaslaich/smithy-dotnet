using System.Text.Json.Serialization;

namespace Bench.Stacks.MinimalApi;

// Property declaration order is load-bearing: System.Text.Json writes properties
// in declaration order, and the suite compares response bodies byte for byte
// against the golden captures. Reordering these changes the wire output.

public sealed class GetItemResponse
{
    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public required int PriceCents { get; init; }
    public required bool InStock { get; init; }
}

public sealed class ItemSummaryDto
{
    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public required int PriceCents { get; init; }
    public required bool InStock { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Tags { get; init; }
}

public sealed class ListItemsResponse
{
    public required IReadOnlyList<ItemSummaryDto> Items { get; init; }
}

public sealed class SearchItemsResponse
{
    public required IReadOnlyList<ItemSummaryDto> Items { get; init; }
}

public sealed class CreateOrderRequest
{
    public required string CustomerId { get; init; }
    public required IReadOnlyList<OrderLineDto> Lines { get; init; }
    public AddressDto? ShippingAddress { get; init; }
    public AddressDto? BillingAddress { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed class OrderLineDto
{
    public required string ItemId { get; init; }
    public required int Quantity { get; init; }
    public required int UnitPriceCents { get; init; }
    public string? Note { get; init; }
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}

public sealed class AddressDto
{
    public required string Line1 { get; init; }
    public string? Line2 { get; init; }
    public required string City { get; init; }
    public string? Region { get; init; }
    public required string PostalCode { get; init; }
    public required string Country { get; init; }
}

public sealed class CreateOrderResponse
{
    public required string OrderId { get; init; }
    public required long TotalCents { get; init; }
    public required int LineCount { get; init; }
}

public sealed class ItemNotFoundResponse
{
    public required string ItemId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
}

/// <summary>
/// Source-generated serialization context.
/// </summary>
/// <remarks>
/// Source generation rather than reflection is the point of this baseline: it is
/// the fastest thing a competent hand-written ASP.NET Core service would
/// realistically do, which is what makes it a fair ceiling for the generated
/// stacks.
/// <para>
/// The default (non-relaxed) encoder is deliberate. NSmithy escapes non-ASCII and
/// quote characters the same way, so leaving this at the default is what keeps
/// the two stacks byte-identical.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GetItemResponse))]
[JsonSerializable(typeof(ListItemsResponse))]
[JsonSerializable(typeof(SearchItemsResponse))]
[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(CreateOrderResponse))]
[JsonSerializable(typeof(ItemNotFoundResponse))]
[JsonSerializable(typeof(ValidationExceptionResponse))]
public sealed partial class MinimalApiJsonContext : JsonSerializerContext;
