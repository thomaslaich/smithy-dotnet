$version: "2"

// Example: an order service demonstrating the bote Kafka protocol.
//
// Clients can produce order commands and consume order lifecycle events.
// Topics are carried by the broker operation traits. Shows message key
// usage and header binding.
namespace example

use bote#command
use bote#event
use bote#kafkaConsume
use bote#kafkaHeader
use bote#kafkaJson
use bote#kafkaKey
use bote#kafkaProduce

/// The order service API.
@title("Order Service API")
@kafkaJson
service OrderService {
    version: "1.0.0"
    operations: [
        SubmitOrder
        ConsumeOrderEvents
    ]
}

/// Submit a new order for processing.
@kafkaProduce(topic: "orders.commands")
operation SubmitOrder {
    input: SubmitOrderCommand
}

/// Consume order lifecycle events.
@kafkaConsume(topic: "orders.events")
operation ConsumeOrderEvents {
    output := {
        events: OrderEvents
    }
}

/// Command to submit a new order.
@command
structure SubmitOrderCommand {
    @kafkaKey
    orderId: String

    /// Propagates distributed trace context via a Kafka header.
    @kafkaHeader(name: "x-trace-id")
    traceId: String

    customerId: String

    @range(min: 0)
    totalCents: Integer
}

/// An order was placed.
@event
structure OrderPlaced {
    @kafkaKey
    orderId: String

    customerId: String

    @range(min: 0)
    totalCents: Integer

    placedAt: Timestamp
}

/// An order was shipped.
@event
structure OrderShipped {
    @kafkaKey
    orderId: String

    carrier: String

    trackingNumber: String

    shippedAt: Timestamp
}

/// An order was cancelled.
@event
structure OrderCancelled {
    @kafkaKey
    orderId: String

    reason: String

    cancelledAt: Timestamp
}

/// The complete event stream offered by the order service.
@streaming
union OrderEvents {
    placed: OrderPlaced
    shipped: OrderShipped
    cancelled: OrderCancelled
}
