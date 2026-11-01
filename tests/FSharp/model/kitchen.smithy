$version: "2"

namespace example.kitchen

use alloy#simpleRestJson
use aws.protocols#restJson1
use smithy.protocols#rpcv2Cbor
use smithy.protocols#rpcv2Json
use example.shared#Money
use smithy.ai#prompts

/// A kitchen that serves dishes.
@restJson1
@simpleRestJson
@rpcv2Cbor
@rpcv2Json
@prompts({
    order_for_table: {
        description: "Order a dish for a table"
        template: "Order {{dish}} for table {{table}}."
        arguments: OrderForTableArguments
    }
})
service Kitchen {
    version: "2024-01-01"
    operations: [Order, Ping]
}

/// Orders a dish for a table.
@http(method: "POST", uri: "/tables/{table}/orders")
operation Order {
    input := {
        @required
        @httpLabel
        table: String

        @httpHeader("x-client-token")
        @idempotencyToken
        clientToken: String

        @required
        dish: Dish
        notes: Notes
        @default("table")
        seating: Seating = "table"
        @default(2)
        guests: Integer = 2
        tip: Money
    }
    output := {
        @required
        receipt: Receipt
    }
    errors: [OutOfStock, Throttled]
}

@readonly
@http(method: "GET", uri: "/ping")
operation Ping {}

structure OrderForTableArguments {
    /// The table to serve.
    @required
    table: String

    dish: String
}

/// A dish on the menu.
structure Dish {
    @required
    name: String

    @required
    ingredients: Ingredients

    sides: Dish

    @default(1.5)
    price: Float

    spice: Spice

    tags: TagMap

    @default([])
    extras: Strings

    blob: Blob

    @timestampFormat("date-time")
    madeAt: Timestamp

    big: BigInteger

    dec: BigDecimal

    @default(-3)
    offset: Long

    type: String

    toString: String
}

list Ingredients {
    member: Ingredient
}

list Strings {
    member: String
}

@sparse
list Notes {
    member: String
}

@sparse
map TagMap {
    key: Ingredient
    value: Integer
}

enum Ingredient {
    BACON
    CHEESE = "cheese"
    @internal
    SECRET_SAUCE = "secret"
    VALUE = "value"
}

enum Seating {
    TABLE = "table"
    BAR = "bar"
}

intEnum Spice {
    MILD = 1
    HOT = 3
}

union Receipt {
    paid: Money
    comped: Unit
    pending: String
    unknown: String
}

/// Out of stock.
@error("client")
@httpError(409)
structure OutOfStock {
    message: String

    @required
    item: Ingredient

    @default(5)
    restockDays: Integer

    data: String
}

@error("server")
@retryable(throttling: true)
structure Throttled {
    @required
    message: String
}
