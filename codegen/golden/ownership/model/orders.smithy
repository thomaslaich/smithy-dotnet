$version: "2"

// A module that owns an operation without knowing the service that serves it.
namespace acme.shop

use smithy.framework#ValidationException

/// Places an order.
@http(method: "POST", uri: "/orders", code: 201)
operation PlaceOrder {
    input := {
        @required
        total: Money
    }

    output := {
        @required
        orderId: String
    }

    errors: [OutOfStock, ValidationException]
}

structure Money {
    @required
    amount: BigDecimal

    @required
    currency: String
}

@error("client")
@httpError(409)
structure OutOfStock {
    @required
    item: String
}
