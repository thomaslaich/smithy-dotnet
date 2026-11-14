$version: "2"

namespace example.shop

use smithy.framework#ValidationException

/// Captures a payment for an order.
@http(method: "POST", uri: "/orders/{orderId}/payments", code: 201)
operation CapturePayment {
    input := {
        @required
        @httpLabel
        orderId: String

        @required
        amount: Money
    }

    output := {
        @required
        paymentId: String
    }

    errors: [OrderNotFound, ValidationException]
}
