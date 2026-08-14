using System.Globalization;
using Bench.Domain;
using Bench.Stacks.MinimalApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bench.Stacks.Mvc;

/// <summary>
/// The hand-written MVC controller baseline.
/// </summary>
/// <remarks>
/// This exists to make the third-party comparison honest. NSmithy generates
/// minimal-API endpoints; the TypeSpec and NSwag emitters generate MVC
/// controllers. Comparing those directly would fold two separate differences ,
/// hosting model, and codec, into one number.
/// <para>
/// With this baseline in the set, the MVC-versus-minimal-API cost can be read
/// off directly (both are hand-written, share DTOs, and share the same
/// source-generated JSON context), and whatever remains between a generated MVC
/// stack and this one is attributable to that stack's own code.
/// </para>
/// </remarks>
[ApiController]
public sealed class BenchmarkController : ControllerBase
{
    private const string Json = "application/json";

    [HttpGet("/items/{itemId}")]
    public IActionResult GetItem(string itemId)
    {
        List<ValidationExceptionField>? failures = null;
        BenchValidation.Collect(ref failures, BenchValidation.ItemId(itemId, "/itemId"), "/itemId");
        if (failures is not null)
            return Invalid(failures);

        var item = BenchDomain.GetItem(itemId);
        if (item is null)
        {
            Response.Headers["X-Amzn-Errortype"] = "ItemNotFound";
            return new JsonResult(
                new ItemNotFoundResponse
                {
                    ItemId = itemId,
                    Message = $"No item with id '{itemId}'.",
                }
            )
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentType = Json,
            };
        }

        return new JsonResult(
            new GetItemResponse
            {
                ItemId = item.ItemId,
                Name = item.Name,
                PriceCents = item.PriceCents,
                InStock = item.InStock,
            }
        )
        {
            ContentType = Json,
        };
    }

    [HttpGet("/items")]
    public IActionResult ListItems([FromQuery] int? count)
    {
        List<ValidationExceptionField>? failures = null;
        if (count is { } c)
            BenchValidation.Collect(
                ref failures,
                BenchValidation.Range(c, 0, 10_000, "/count"),
                "/count"
            );
        if (failures is not null)
            return Invalid(failures);

        var items = BenchDomain.ListItems(count);
        return new JsonResult(new ListItemsResponse { Items = ToSummaries(items) })
        {
            ContentType = Json,
        };
    }

    [HttpGet("/search/items")]
    public IActionResult SearchItems(
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
            return Invalid(failures);

        var result = BenchDomain.Search(q, category, minPriceCents, maxPriceCents, sort, tags);

        Response.Headers["x-total-count"] = result.TotalCount.ToString(
            CultureInfo.InvariantCulture
        );

        return new JsonResult(new SearchItemsResponse { Items = ToSummaries(result.Items) })
        {
            ContentType = Json,
        };
    }

    [HttpPost("/orders")]
    public IActionResult CreateOrder([FromBody] CreateOrderRequest order)
    {
        var lines = order.Lines;

        // Every line is checked, matching what NSmithy derives from the model.
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
            return Invalid(failures);

        var domainLines = new OrderLineInput[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            domainLines[i] = new OrderLineInput(line.ItemId, line.Quantity, line.UnitPriceCents);
        }

        var result = BenchDomain.CreateOrder(order.CustomerId, domainLines);

        return new JsonResult(
            new CreateOrderResponse
            {
                OrderId = result.OrderId,
                TotalCents = result.TotalCents,
                LineCount = result.LineCount,
            }
        )
        {
            StatusCode = StatusCodes.Status201Created,
            ContentType = Json,
        };
    }

    /// <summary>Renders the ValidationException exactly as restJson1 puts it on the wire.</summary>
    private JsonResult Invalid(List<ValidationExceptionField> failures)
    {
        Response.Headers["X-Amzn-Errortype"] = "ValidationException";
        return new JsonResult(BenchValidation.Build(failures))
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentType = Json,
        };
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
