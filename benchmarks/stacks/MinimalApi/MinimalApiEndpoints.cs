using System.Globalization;
using System.Text.Json;
using Bench.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bench.Stacks.MinimalApi;

/// <summary>
/// The hand-written ASP.NET Core minimal-API baseline.
/// </summary>
/// <remarks>
/// This is the ceiling, not a competitor: it is what a competent engineer would
/// write by hand for this contract, with source-generated JSON and no
/// reflection. Generated stacks are interesting to the extent they come close to
/// it.
/// <para>
/// It reproduces restJson1's wire details exactly, bare <c>application/json</c>
/// with no charset, the <c>X-Amzn-Errortype</c> discriminator on errors, 201 on
/// create. A hand-written service would not invent those conventions on its own,
/// but matching them is what makes the comparison a comparison; the cost of
/// setting one extra header is noise.
/// </para>
/// </remarks>
public static class MinimalApiEndpoints
{
    private const string Json = "application/json";

    public static IEndpointRouteBuilder MapBenchmarkMinimalApi(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/items/{itemId}", GetItem);
        routes.MapGet("/items", ListItems);
        routes.MapGet("/search/items", SearchItems);
        // Cast required: a Task-returning method taking HttpContext otherwise
        // binds to the RequestDelegate overload, which discards the result.
        routes.MapPost("/orders", (Delegate)CreateOrderAsync);

        return routes;
    }

    private static Results<
        JsonHttpResult<GetItemResponse>,
        JsonHttpResult<ItemNotFoundResponse>,
        JsonHttpResult<ValidationExceptionResponse>
    > GetItem(string itemId, HttpContext context)
    {
        List<ValidationExceptionField>? failures = null;
        BenchValidation.Collect(ref failures, BenchValidation.ItemId(itemId, "/itemId"), "/itemId");
        if (failures is not null)
            return Invalid(context, failures);

        var item = BenchDomain.GetItem(itemId);
        if (item is null)
        {
            context.Response.Headers["X-Amzn-Errortype"] = "ItemNotFound";
            return TypedResults.Json(
                new ItemNotFoundResponse
                {
                    ItemId = itemId,
                    Message = $"No item with id '{itemId}'.",
                },
                MinimalApiJsonContext.Default.ItemNotFoundResponse,
                contentType: Json,
                statusCode: StatusCodes.Status404NotFound
            );
        }

        return TypedResults.Json(
            new GetItemResponse
            {
                ItemId = item.ItemId,
                Name = item.Name,
                PriceCents = item.PriceCents,
                InStock = item.InStock,
            },
            MinimalApiJsonContext.Default.GetItemResponse,
            contentType: Json
        );
    }

    private static Results<
        JsonHttpResult<ListItemsResponse>,
        JsonHttpResult<ValidationExceptionResponse>
    > ListItems([FromQuery] int? count, HttpContext context)
    {
        List<ValidationExceptionField>? failures = null;
        if (count is { } c)
            BenchValidation.Collect(
                ref failures,
                BenchValidation.Range(c, 0, 10_000, "/count"),
                "/count"
            );
        if (failures is not null)
            return Invalid(context, failures);

        var items = BenchDomain.ListItems(count);
        return TypedResults.Json(
            new ListItemsResponse { Items = ToSummaries(items) },
            MinimalApiJsonContext.Default.ListItemsResponse,
            contentType: Json
        );
    }

    private static Results<
        JsonHttpResult<SearchItemsResponse>,
        JsonHttpResult<ValidationExceptionResponse>
    > SearchItems(
        HttpContext context,
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] int? minPriceCents,
        [FromQuery] int? maxPriceCents,
        [FromQuery] string? sort,
        [FromQuery] string[]? tags,
        [FromHeader(Name = "x-request-id")] string? requestId,
        [FromHeader(Name = "x-tenant-id")] string? tenantId,
        [FromHeader(Name = "x-correlation-id")] string? correlationId,
        [FromHeader(Name = "x-client-version")] string? clientVersion
    )
    {
        // Declaration order matters: the aggregate message lists failures in the
        // order the model declares the members.
        List<ValidationExceptionField>? failures = null;
        if (minPriceCents is { } min)
            BenchValidation.Collect(
                ref failures,
                BenchValidation.Range(min, 0, 100_000_000, "/minPriceCents"),
                "/minPriceCents"
            );
        if (maxPriceCents is { } max)
            BenchValidation.Collect(
                ref failures,
                BenchValidation.Range(max, 0, 100_000_000, "/maxPriceCents"),
                "/maxPriceCents"
            );
        if (failures is not null)
            return Invalid(context, failures);

        var result = BenchDomain.Search(q, category, minPriceCents, maxPriceCents, sort, tags);

        context.Response.Headers["x-total-count"] = result.TotalCount.ToString(
            CultureInfo.InvariantCulture
        );

        return TypedResults.Json(
            new SearchItemsResponse { Items = ToSummaries(result.Items) },
            MinimalApiJsonContext.Default.SearchItemsResponse,
            contentType: Json
        );
    }

    private static async Task<IResult> CreateOrderAsync(HttpContext context)
    {
        var order = await JsonSerializer.DeserializeAsync(
            context.Request.Body,
            MinimalApiJsonContext.Default.CreateOrderRequest,
            context.RequestAborted
        );

        ArgumentNullException.ThrowIfNull(order);

        var lines = order.Lines;

        // Every line is checked, which is what NSmithy does from the model. On the
        // 1 MB payload this is 6,400 regex matches plus 12,800 range checks, and
        // leaving it out is what would make this baseline unfairly cheap.
        List<ValidationExceptionField>? failures = null;
        BenchValidation.Collect(
            ref failures,
            BenchValidation.Length(order.CustomerId, 1, 64, "/customerId"),
            "/customerId"
        );
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = string.Create(CultureInfo.InvariantCulture, $"/lines/{i}");
            BenchValidation.Collect(
                ref failures,
                BenchValidation.ItemId(line.ItemId, $"{prefix}/itemId"),
                $"{prefix}/itemId"
            );
            BenchValidation.Collect(
                ref failures,
                BenchValidation.Range(line.Quantity, 1, 1_000, $"{prefix}/quantity"),
                $"{prefix}/quantity"
            );
            BenchValidation.Collect(
                ref failures,
                BenchValidation.Range(
                    line.UnitPriceCents,
                    0,
                    100_000_000,
                    $"{prefix}/unitPriceCents"
                ),
                $"{prefix}/unitPriceCents"
            );
        }

        if (failures is not null)
            return Invalid(context, failures);

        var domainLines = new OrderLineInput[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            domainLines[i] = new OrderLineInput(line.ItemId, line.Quantity, line.UnitPriceCents);
        }

        var result = BenchDomain.CreateOrder(order.CustomerId, domainLines);

        return TypedResults.Json(
            new CreateOrderResponse
            {
                OrderId = result.OrderId,
                TotalCents = result.TotalCents,
                LineCount = result.LineCount,
            },
            MinimalApiJsonContext.Default.CreateOrderResponse,
            contentType: Json,
            statusCode: StatusCodes.Status201Created
        );
    }

    /// <summary>Renders the ValidationException exactly as restJson1 puts it on the wire.</summary>
    private static JsonHttpResult<ValidationExceptionResponse> Invalid(
        HttpContext context,
        List<ValidationExceptionField> failures
    )
    {
        context.Response.Headers["X-Amzn-Errortype"] = "ValidationException";
        return TypedResults.Json(
            BenchValidation.Build(failures),
            MinimalApiJsonContext.Default.ValidationExceptionResponse,
            contentType: Json,
            statusCode: StatusCodes.Status400BadRequest
        );
    }

    private static ItemSummaryDto[] ToSummaries(ArraySegment<BenchItem> items)
    {
        var summaries = new ItemSummaryDto[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            summaries[i] = new ItemSummaryDto
            {
                ItemId = item.ItemId,
                Name = item.Name,
                PriceCents = item.PriceCents,
                InStock = item.InStock,
                Category = item.Category,
                Tags = item.Tags,
            };
        }

        return summaries;
    }
}
