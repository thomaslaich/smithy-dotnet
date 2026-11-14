$version: "2"

namespace example.shop

use smithy.framework#ValidationException

/// Places an order for the given total.
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
    errors: [ValidationException]
}
