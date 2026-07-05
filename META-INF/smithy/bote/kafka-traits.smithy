$version: "2"

namespace bote

/// Marks an operation as a produce capability: clients may produce the
/// operation's input — a @command structure — to the given Kafka topic.
/// Produce operations must not define an output.
@trait(
    selector: "operation"
    conflicts: [bote#kafkaConsume]
)
structure kafkaProduce {
    /// The Kafka topic name.
    @required
    topic: String

    /// Whether the topic uses log compaction: only the latest message per
    /// key is retained, giving the channel table semantics rather than log
    /// semantics. This is a contract-level promise consumers rely on, which
    /// is why it lives here and not in bote.infra#kafkaTopicConfig.
    /// Defaults to false.
    compacted: Boolean
}

/// Marks an operation as a consume capability: clients may consume the
/// operation's events from the given Kafka topic. The operation output must
/// contain a member targeting a @streaming union whose members are @event
/// structures.
@trait(
    selector: "operation"
    conflicts: [bote#kafkaProduce]
)
structure kafkaConsume {
    /// The Kafka topic name.
    @required
    topic: String

    /// Whether the topic uses log compaction. Must agree with every other
    /// operation on the same topic. Defaults to false.
    compacted: Boolean
}

/// Marks a structure member as the Kafka message key.
/// Only one member per structure may carry this trait.
/// The member must be a simple type (String, Integer, Long, etc.).
@trait(selector: "structure > member")
structure kafkaKey {}

/// Maps a structure member to a Kafka message header.
@trait(selector: "structure > member")
structure kafkaHeader {
    /// The Kafka header name.
    @required
    name: String
}
