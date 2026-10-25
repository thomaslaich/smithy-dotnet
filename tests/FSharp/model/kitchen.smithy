$version: "2"

namespace example.kitchen

use example.shared#Money

/// A kitchen that serves dishes.
service Kitchen {
    version: "2024-01-01"
    operations: [Order, Ping]
}

operation Order {
    input := {
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

operation Ping {}

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

    doc: Document

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
