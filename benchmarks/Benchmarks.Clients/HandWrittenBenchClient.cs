using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bench.Stacks.MinimalApi;

namespace Bench.Clients;

/// <summary>
/// The hand-written <see cref="HttpClient"/> + source-generated JSON client, the
/// ceiling for the client suite.
/// </summary>
/// <remarks>
/// Same role the hand-written minimal API plays on the server side, and it reuses
/// that stack's DTOs and <see cref="MinimalApiJsonContext"/> so both halves of the
/// baseline are the same quality of hand-written.
/// <para>
/// Responses are read with <see cref="HttpCompletionOption.ResponseHeadersRead"/>
/// so deserialization streams straight off the response. The default,
/// <c>ResponseContentRead</c>, buffers the whole body first, which cost this
/// client roughly 3 MB of extra allocation on the 10,000-item response and made
/// it lose to a generated client. A ceiling that buffers is not a ceiling.
/// <para>
/// Query strings are built by hand into a pooled <see cref="StringBuilder"/> and
/// path labels are escaped with <see cref="Uri.EscapeDataString"/>, which is what
/// a competent implementation would do. Nothing is precomputed or cached across
/// calls: the point is to measure per-call client work, and hoisting it out would
/// make this baseline unreachable rather than merely hard to reach.
/// </para>
/// </remarks>
public sealed class HandWrittenBenchClient : IBenchClient
{
    private readonly HttpClient httpClient;

    public HandWrittenBenchClient(HttpClient httpClient) => this.httpClient = httpClient;

    public string Name => "hand-written";

    public async Task<BenchItemResult> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default
    )
    {
        using var request = NewRequest(HttpMethod.Get, $"/items/{Uri.EscapeDataString(itemId)}");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync(
            MinimalApiJsonContext.Default.GetItemResponse,
            cancellationToken
        );

        return payload is null
            ? throw new InvalidOperationException("GetItem returned an empty body.")
            : new BenchItemResult(
                payload.ItemId,
                payload.Name,
                payload.PriceCents,
                payload.InStock
            );
    }

    public async Task<BenchListResult> ListItemsAsync(
        int? count,
        CancellationToken cancellationToken = default
    )
    {
        var uri = count is { } c
            ? string.Create(CultureInfo.InvariantCulture, $"/items?count={c}")
            : "/items";

        using var request = NewRequest(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync(
            MinimalApiJsonContext.Default.ListItemsResponse,
            cancellationToken
        );

        var items = payload?.Items ?? [];
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

        var query = new StringBuilder("/search/items");
        var first = true;
        Append(query, ref first, "q", input.Query);
        Append(query, ref first, "category", input.Category);
        Append(query, ref first, "minPriceCents", input.MinPriceCents);
        Append(query, ref first, "maxPriceCents", input.MaxPriceCents);
        Append(query, ref first, "sort", input.Sort);
        if (input.Tags is { } tags)
        {
            for (var i = 0; i < tags.Count; i++)
                Append(query, ref first, "tags", tags[i]);
        }

        using var request = NewRequest(HttpMethod.Get, query.ToString());
        AddHeader(request, "x-request-id", input.RequestId);
        AddHeader(request, "x-tenant-id", input.TenantId);
        AddHeader(request, "x-correlation-id", input.CorrelationId);
        AddHeader(request, "x-client-version", input.ClientVersion);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync(
            MinimalApiJsonContext.Default.SearchItemsResponse,
            cancellationToken
        );

        var totalCount = 0;
        if (
            response.Headers.TryGetValues("x-total-count", out var values)
            && int.TryParse(
                values.FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            totalCount = parsed;
        }

        return new BenchSearchResult(payload?.Items.Count ?? 0, totalCount);
    }

    public async Task<BenchOrderResult> CreateOrderAsync(
        BenchOrderInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = new OrderLineDto[input.Lines.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = input.Lines[i];
            lines[i] = new OrderLineDto
            {
                ItemId = line.ItemId,
                Quantity = line.Quantity,
                UnitPriceCents = line.UnitPriceCents,
                Note = line.Note,
                Attributes = line.Attributes,
            };
        }

        var body = new CreateOrderRequest
        {
            CustomerId = input.CustomerId,
            Lines = lines,
            ShippingAddress = ToAddress(input.ShippingAddress),
            BillingAddress = ToAddress(input.BillingAddress),
            Metadata = input.Metadata,
        };

        using var content = new ByteArrayContent(
            JsonSerializer.SerializeToUtf8Bytes(
                body,
                MinimalApiJsonContext.Default.CreateOrderRequest
            )
        );
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var request = NewRequest(HttpMethod.Post, "/orders");
        request.Content = content;
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync(
            MinimalApiJsonContext.Default.CreateOrderResponse,
            cancellationToken
        );

        return payload is null
            ? throw new InvalidOperationException("CreateOrder returned an empty body.")
            : new BenchOrderResult(payload.OrderId, payload.TotalCents, payload.LineCount);
    }

    private static AddressDto? ToAddress(BenchAddress? address) =>
        address is null
            ? null
            : new AddressDto
            {
                Line1 = address.Line1,
                Line2 = address.Line2,
                City = address.City,
                Region = address.Region,
                PostalCode = address.PostalCode,
                Country = address.Country,
            };

    /// <summary>
    /// Builds a request with the Accept header every operation sends. The client
    /// parity gate compares emitted headers, and NSmithy's client advertises the
    /// protocol content type on each request, a baseline that omitted it would
    /// be sending different bytes and doing marginally less work.
    /// </summary>
    private static HttpRequestMessage NewRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private static void AddHeader(HttpRequestMessage request, string name, string? value)
    {
        if (value is not null)
            request.Headers.TryAddWithoutValidation(name, value);
    }

    private static void Append(StringBuilder query, ref bool first, string name, string? value)
    {
        if (value is null)
            return;

        query
            .Append(first ? '?' : '&')
            .Append(name)
            .Append('=')
            .Append(Uri.EscapeDataString(value));
        first = false;
    }

    private static void Append(StringBuilder query, ref bool first, string name, int? value)
    {
        if (value is not { } v)
            return;

        query.Append(first ? '?' : '&').Append(name).Append('=');
        query.Append(v.ToString(CultureInfo.InvariantCulture));
        first = false;
    }

    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
