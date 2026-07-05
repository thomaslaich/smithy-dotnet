$version: "2"

namespace bote

// Broker-agnostic core. These traits carry no Kafka- or Redis-specific meaning:
// they classify message payloads by their role in the contract. Operations are
// bound to a broker with per-broker operation traits (@kafkaProduce,
// @redisSubscribe, ...), which carry the channel address — mirroring how
// Smithy's HTTP and MQTT bindings speak their transport's language while the
// shapes stay transport-neutral.
/// Marks a payload structure as an event message.
@trait(
    selector: "structure"
    conflicts: [bote#command, bote#reply]
)
structure event {}

/// Marks a payload structure as a command message.
@trait(
    selector: "structure"
    conflicts: [bote#event, bote#reply]
)
structure command {}

/// Marks a payload structure as a reply message.
///
/// Reserved vocabulary: no current protocol supports replies. Request-reply
/// needs broker-native reply plumbing (a reply address and correlation id,
/// as in AMQP's reply_to/correlation_id); Kafka and Redis only offer it by
/// convention, so their protocols do not include this trait. It will be
/// wired into a future protocol with first-class reply semantics.
@trait(
    selector: "structure"
    conflicts: [bote#event, bote#command]
)
structure reply {}
