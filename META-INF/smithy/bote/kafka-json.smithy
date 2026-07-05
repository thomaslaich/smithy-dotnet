$version: "2"

namespace bote

use smithy.api#protocolDefinition

/// A Smithy protocol for Kafka using JSON serialization.
///
/// Services annotated with this trait exchange JSON-encoded messages over
/// Kafka. Operations must be annotated with @kafkaProduce or @kafkaConsume,
/// which carry the topic.
///
/// Wire rules:
///
/// - A @command message value is the bare JSON serialization of its structure.
/// - An @event message value is serialized according to eventDiscrimination,
///   so consumers of a multi-event channel can tell event types apart.
/// - Members annotated with @kafkaHeader travel only as Kafka headers and are
///   never serialized into the JSON value.
/// - The member annotated with @kafkaKey is serialized both as the Kafka
///   message key and as a field of the JSON value.
@protocolDefinition(
    traits: [bote#kafkaProduce, bote#kafkaConsume, bote#event, bote#command, bote#kafkaKey, bote#kafkaHeader]
)
@trait(selector: "service")
structure kafkaJson {
    /// How the event type is identified on the wire.
    ///
    /// A @kafkaConsume channel can carry every member of its @streaming
    /// union, so consumers need a way to tell which event type a given
    /// message is. Producer and consumer must agree on this mechanism —
    /// it is part of the protocol contract.
    ///
    /// Defaults to ENVELOPE.
    eventDiscrimination: EventDiscrimination
}

/// How JSON event messages carry their type on a multi-event channel.
enum EventDiscrimination {
    /// The value is wrapped in a single-key object whose key is the
    /// @streaming union member name — identical to how restJson1
    /// serializes tagged unions:
    ///
    ///     {"placed": {"orderId": "42", ...}}
    ///
    /// This is the default: codegen consumers that already implement
    /// Smithy JSON union codecs get it for free.
    ENVELOPE

    /// The value is the bare JSON payload; a Kafka header named
    /// "bote-type" carries the @streaming union member name.
    ///
    /// Matches prevailing Kafka practice (type headers) and keeps payloads
    /// readable for non-Smithy consumers, at the cost of a bote-specific
    /// header convention.
    HEADER

    /// No discriminator on the wire. Each @kafkaConsume's @streaming union
    /// may declare at most one event type (validator-enforced), so the
    /// channel is unambiguous by construction.
    NONE
}
