$version: "2"

namespace nsmithy.bench

use aws.protocols#restJson1

/// The contract under benchmark.
///
/// This model is the single source of truth for every stack in the suite. It is
/// emitted to OpenAPI 3 (see `SmithyOpenApiProtocol` in the NSmithy stack
/// project), and that OpenAPI document is what the TypeSpec stack is imported
/// from, so all stacks serve a byte-identical wire contract.
///
/// Each operation isolates one cost centre. Keep them that way: an operation
/// that mixes, say, a large payload with heavy header binding cannot tell you
/// which of the two regressed.
///
/// Constraint traits are present and enforced. Every stack in the suite must do
/// equivalent validation work, or the comparison measures who skips checks
/// rather than who is fast, the hand-written baselines validate by hand to
/// match what NSmithy derives from the model.
///
/// The constraints are chosen so every success scenario in the corpus passes
/// them; only the dedicated validation scenario violates one. That keeps the
/// happy-path numbers measuring the happy path.
@restJson1
@title("NSmithy Benchmark Service")
service BenchmarkService {
    version: "2026-08-11"
    operations: [
        GetItem
        SearchItems
        ListItems
        CreateOrder
    ]
}

/// Scenario: minimal work. One path label in, four scalar members out.
///
/// This is the floor, whatever this costs is close to pure framework and
/// codec overhead, with almost no payload to amortise it against.
@readonly
@http(method: "GET", uri: "/items/{itemId}")
operation GetItem {
    input := {
        @required
        @httpLabel
        itemId: ItemId
    }

    output := {
        @required
        itemId: ItemId

        @required
        name: String

        @required
        priceCents: PriceCents

        @required
        inStock: Boolean
    }

    errors: [
        ItemNotFound
    ]
}

/// Scenario: HTTP binding cost, not body cost.
///
/// Six query parameters (one of them a list, one an enum) and four headers, in;
/// one bound response header, out. The body stays small on purpose so the
/// numbers reflect query/header parsing and serialization.
@readonly
@http(method: "GET", uri: "/search/items")
operation SearchItems {
    input := {
        @httpQuery("q")
        query: String

        @httpQuery("category")
        category: String

        @httpQuery("minPriceCents")
        minPriceCents: PriceCents

        @httpQuery("maxPriceCents")
        maxPriceCents: PriceCents

        @httpQuery("sort")
        sort: SortOrder

        @httpQuery("tags")
        tags: StringList

        @httpHeader("x-request-id")
        requestId: String

        @httpHeader("x-tenant-id")
        tenantId: String

        @httpHeader("x-correlation-id")
        correlationId: String

        @httpHeader("x-client-version")
        clientVersion: String
    }

    output := {
        @required
        items: ItemSummaries

        @required
        @httpHeader("x-total-count")
        totalCount: Integer
    }
}

/// Scenario: response body scaling.
///
/// `count` controls how many summaries come back. The corpus drives this at
/// 1 / 100 / 10000 to separate per-request fixed cost from per-element cost.
@readonly
@http(method: "GET", uri: "/items")
operation ListItems {
    input := {
        @httpQuery("count")
        count: ItemCount
    }

    output := {
        @required
        items: ItemSummaries
    }
}

/// Scenario: large nested request body.
///
/// Deep-ish structure with two nested addresses, a map, and a list of line
/// items that themselves nest. The corpus drives this to roughly 1 MB so
/// deserialization dominates and buffer/allocation behaviour shows up.
@http(method: "POST", uri: "/orders", code: 201)
operation CreateOrder {
    input := {
        @required
        customerId: CustomerId

        @required
        lines: OrderLines

        shippingAddress: Address

        billingAddress: Address

        metadata: StringMap
    }

    output := {
        @required
        orderId: String

        @required
        totalCents: Long

        @required
        lineCount: Integer
    }
}

structure ItemSummary {
    @required
    itemId: ItemId

    @required
    name: String

    @required
    priceCents: PriceCents

    @required
    inStock: Boolean

    category: String

    tags: StringList
}

structure OrderLine {
    @required
    itemId: ItemId

    @required
    quantity: Quantity

    @required
    unitPriceCents: PriceCents

    note: String

    attributes: StringMap
}

structure Address {
    @required
    line1: String

    line2: String

    @required
    city: String

    region: String

    @required
    postalCode: String

    @required
    country: String
}

enum SortOrder {
    PRICE_ASC = "priceAsc"
    PRICE_DESC = "priceDesc"
    NAME_ASC = "nameAsc"
    RELEVANCE = "relevance"
}

/// Catalog identifier. The pattern matches every id the corpus uses, including
/// the deliberate miss (`item-99999`), so a lookup miss still returns 404 rather
/// than being rejected as malformed.
@pattern("^item-[0-9]{5}$")
string ItemId

/// Page size. The corpus drives 1 / 100 / 10000 through this, and the
/// validation scenario drives 99999 past the maximum.
@range(min: 0, max: 10000)
integer ItemCount

@range(min: 1, max: 1000)
integer Quantity

@range(min: 0, max: 100000000)
integer PriceCents

@length(min: 1, max: 64)
string CustomerId

list ItemSummaries {
    member: ItemSummary
}

list OrderLines {
    member: OrderLine
}

list StringList {
    member: String
}

map StringMap {
    key: String
    value: String
}

/// Scenario: modeled error path.
///
/// Exercises error serialization, which on restJson1 means a distinct status
/// code plus the error shape discriminator, a path that ordinary success
/// benchmarks never touch.
@error("client")
@httpError(404)
structure ItemNotFound {
    @required
    itemId: ItemId

    message: String
}
