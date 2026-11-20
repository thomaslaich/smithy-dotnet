$version: "2"

// A service with a client and a server: HTTP bindings, constraints, operation and service errors.
namespace example.kitchen

use aws.protocols#restJson1
use smithy.framework#ValidationException

/// A kitchen that takes orders.
@restJson1
service Kitchen {
    version: "2026-01-01"
    operations: [PlaceOrder, GetOrder, Ping]
    errors: [Closed]
}

/// Places an order for a table.
@idempotent
@http(method: "PUT", uri: "/tables/{table}/orders", code: 201)
operation PlaceOrder {
    input := {
        @required
        @httpLabel
        table: String

        @httpHeader("x-client-token")
        @idempotencyToken
        token: String

        @required
        @length(min: 1)
        dish: String

        @default(1)
        @range(min: 1, max: 10)
        portions: Integer
    }

    output := {
        @required
        orderId: String
    }

    errors: [SoldOut, ValidationException]
}

@readonly
@http(method: "GET", uri: "/orders/{orderId}")
operation GetOrder {
    input := {
        @required
        @httpLabel
        orderId: String

        @httpQuery("verbose")
        verbose: Boolean
    }

    output := {
        @required
        dish: String

        status: OrderStatus
    }

    errors: [NoSuchOrder, ValidationException]
}

@readonly
@http(method: "GET", uri: "/ping")
operation Ping {}

enum OrderStatus {
    QUEUED
    SERVED
}

@error("client")
@httpError(409)
structure SoldOut {
    @required
    dish: String
}

@error("client")
@httpError(404)
structure NoSuchOrder {
    @required
    orderId: String
}

/// The kitchen does not take orders right now.
@error("server")
@httpError(503)
structure Closed {
    message: String
}
