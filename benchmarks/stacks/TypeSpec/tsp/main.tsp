import "@typespec/http";
import "@typespec/openapi";
import "@typespec/openapi3";

using Http;
using OpenAPI;

/**
 * The contract under benchmark.
 *
 * This model is the single source of truth for every stack in the suite. It is
 * emitted to OpenAPI 3 (see `SmithyOpenApiProtocol` in the NSmithy stack
 * project), and that OpenAPI document is what the TypeSpec stack is imported
 * from, so all stacks serve a byte-identical wire contract.
 *
 * Each operation isolates one cost centre. Keep them that way: an operation
 * that mixes, say, a large payload with heavy header binding cannot tell you
 * which of the two regressed.
 *
 * Constraint traits are present and enforced. Every stack in the suite must do
 * equivalent validation work, or the comparison measures who skips checks
 * rather than who is fast, the hand-written baselines validate by hand to
 * match what NSmithy derives from the model.
 *
 * The constraints are chosen so every success scenario in the corpus passes
 * them; only the dedicated validation scenario violates one. That keeps the
 * happy-path numbers measuring the happy path.
 */
@service(#{ title: "NSmithy Benchmark Service" })
@info(#{ version: "2026-08-11" })
namespace NSmithyBenchmarkService;

model Address {
  line1: string;
  line2?: string;
  city: string;
  region?: string;
  postalCode: string;
  country: string;
}

model CreateOrderRequestContent {
  @minLength(1) @maxLength(64) customerId: string;
  lines: OrderLine[];
  shippingAddress?: Address;
  billingAddress?: Address;
  metadata?: StringMap;
}

model CreateOrderResponseContent {
  orderId: string;
  totalCents: int64;
  lineCount: int32;
}

model GetItemResponseContent {
  /**
   * Catalog identifier. The pattern matches every id the corpus uses, including
   * the deliberate miss (`item-99999`), so a lookup miss still returns 404 rather
   * than being rejected as malformed.
   */
  @pattern("^item-[0-9]{5}$") itemId: string;

  name: string;
  @minValue(0) @maxValue(100000000) priceCents: int32;
  inStock: boolean;
}

/**
 * Scenario: modeled error path.
 *
 * Exercises error serialization, which on restJson1 means a distinct status
 * code plus the error shape discriminator, a path that ordinary success
 * benchmarks never touch.
 */
@error
model ItemNotFoundResponseContent {
  /**
   * Catalog identifier. The pattern matches every id the corpus uses, including
   * the deliberate miss (`item-99999`), so a lookup miss still returns 404 rather
   * than being rejected as malformed.
   */
  @pattern("^item-[0-9]{5}$") itemId: string;

  message?: string;
}

model ItemSummary {
  /**
   * Catalog identifier. The pattern matches every id the corpus uses, including
   * the deliberate miss (`item-99999`), so a lookup miss still returns 404 rather
   * than being rejected as malformed.
   */
  @pattern("^item-[0-9]{5}$") itemId: string;

  name: string;
  @minValue(0) @maxValue(100000000) priceCents: int32;
  inStock: boolean;
  category?: string;
  tags?: string[];
}

model ListItemsResponseContent {
  items: ItemSummary[];
}

model OrderLine {
  /**
   * Catalog identifier. The pattern matches every id the corpus uses, including
   * the deliberate miss (`item-99999`), so a lookup miss still returns 404 rather
   * than being rejected as malformed.
   */
  @pattern("^item-[0-9]{5}$") itemId: string;

  @minValue(1) @maxValue(1000) quantity: int32;
  @minValue(0) @maxValue(100000000) unitPriceCents: int32;
  note?: string;
  attributes?: StringMap;
}

model SearchItemsResponseContent {
  items: ItemSummary[];
}

enum SortOrder {
  priceAsc,
  priceDesc,
  nameAsc,
  relevance,
}

model StringMap {
  ...Record<string>;
}

/**
 * Scenario: response body scaling.
 *
 * `count` controls how many summaries come back. The corpus drives this at
 * 1 / 100 / 10000 to separate per-request fixed cost from per-element cost.
 */
@route("/items") @get op ListItems(
  /**
   * Page size. The corpus drives 1 / 100 / 10000 through this, and the
   * validation scenario drives 99999 past the maximum.
   */
  @minValue(0)
  @maxValue(10000)
  @query(#{ explode: true })
  count?: int32,
): ListItemsResponseContent;

/**
 * Scenario: minimal work. One path label in, four scalar members out.
 *
 * This is the floor, whatever this costs is close to pure framework and
 * codec overhead, with almost no payload to amortise it against.
 */
@route("/items/{itemId}") @get op GetItem(
  /**
   * Catalog identifier. The pattern matches every id the corpus uses, including
   * the deliberate miss (`item-99999`), so a lookup miss still returns 404 rather
   * than being rejected as malformed.
   */
  @pattern("^item-[0-9]{5}$") @path itemId: string,
): GetItemResponseContent | (NotFoundResponse & ItemNotFoundResponseContent);

/**
 * Scenario: large nested request body.
 *
 * Deep-ish structure with two nested addresses, a map, and a list of line
 * items that themselves nest. The corpus drives this to roughly 1 MB so
 * deserialization dominates and buffer/allocation behaviour shows up.
 */
@route("/orders") @post op CreateOrder(@body body: CreateOrderRequestContent): CreatedResponse &
  CreateOrderResponseContent;

/**
 * Scenario: HTTP binding cost, not body cost.
 *
 * Six query parameters (one of them a list, one an enum) and four headers, in;
 * one bound response header, out. The body stays small on purpose so the
 * numbers reflect query/header parsing and serialization.
 */
@route("/search/items") @get op SearchItems(
  @query(#{ explode: true }) q?: string,
  @query(#{ explode: true }) category?: string,

  @minValue(0)
  @maxValue(100000000)
  @query(#{ explode: true })
  minPriceCents?: int32,

  @minValue(0)
  @maxValue(100000000)
  @query(#{ explode: true })
  maxPriceCents?: int32,

  @query(#{ explode: true }) sort?: SortOrder,
  @query(#{ explode: true }) tags?: string[],
  @header `x-client-version`?: string,
  @header `x-correlation-id`?: string,
  @header `x-request-id`?: string,
  @header `x-tenant-id`?: string,
): {
  @header("x-total-count") xTotalCount: int32;
  @body body: SearchItemsResponseContent;
};
