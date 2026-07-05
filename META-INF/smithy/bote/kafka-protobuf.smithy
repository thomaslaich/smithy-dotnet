$version: "2"

namespace bote

use smithy.api#protocolDefinition

/// A Smithy protocol for Kafka using Protocol Buffers serialization.
///
/// Applications exchange protobuf-encoded messages. The Smithy shape graph is
/// mapped to .proto message definitions by code generators that consume this
/// protocol.
///
/// Proto field numbering and wire-type annotations are provided by the Alloy
/// trait library (alloy.proto#protoIndex, alloy.proto#protoNumType, etc.).
/// Depend on com.disneystreaming.alloy:alloy-core alongside bote.
///
/// A Schema Registry is not required — .proto files serve as the schema
/// contract. If a Confluent-compatible or Buf Schema Registry is used,
/// the registry URL and credentials are runtime configuration and must
/// not appear in the model.
///
/// Operations must use @kafkaProduce or @kafkaConsume, which carry the topic.
@protocolDefinition(
    traits: [
        bote#kafkaProduce
        bote#kafkaConsume
        bote#event
        bote#command
        bote#kafkaKey
        bote#kafkaHeader
        alloy.proto#protoIndex
        alloy.proto#protoNumType
        alloy.proto#protoWrapped
        alloy.proto#protoTimestampFormat
        alloy.proto#protoEnumFormat
        alloy.proto#protoCompactUUID
    ]
)
@trait(selector: "service")
structure kafkaProtobuf {
    /// How schema subject names are derived when a Confluent-compatible
    /// Schema Registry is used. Has no effect if no registry is configured.
    ///
    /// Defaults to TOPIC_NAME.
    subjectNamingStrategy: SubjectNamingStrategy
}
