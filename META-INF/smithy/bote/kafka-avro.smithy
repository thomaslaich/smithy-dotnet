$version: "2"

namespace bote

use smithy.api#protocolDefinition

/// A Smithy protocol for Kafka using Avro serialization via a Schema Registry.
///
/// Producers and consumers exchange Avro-encoded messages following the
/// Confluent wire format (magic byte + schema ID prefix + Avro payload).
/// Schema subjects are registered in and resolved from a Schema Registry
/// at runtime — the registry URL and credentials are not part of the
/// contract and must not appear in the model.
///
/// Operations in a service annotated with @kafkaAvro must use @kafkaProduce
/// or @kafkaConsume, which carry the topic.
///
/// Event discrimination is handled by the wire format itself: the schema ID
/// prefix identifies the writer schema, and thus the event type, of every
/// message. Multi-event channels require a subjectNamingStrategy that permits
/// multiple schemas per topic (RECORD_NAME or TOPIC_RECORD_NAME).
@protocolDefinition(
    traits: [
        bote#kafkaProduce
        bote#kafkaConsume
        bote#event
        bote#command
        bote#kafkaKey
        bote#kafkaHeader
        bote#avroCompatibility
    ]
)
@trait(selector: "service")
structure kafkaAvro {
    /// How schema subject names are derived from topic and record names.
    ///
    /// Determines the subject name used when registering or resolving a schema
    /// in the Schema Registry. Producer and consumer must agree on this strategy
    /// or they will resolve different subjects and fail to deserialize.
    ///
    /// Defaults to TOPIC_NAME, which is the Confluent Schema Registry default.
    subjectNamingStrategy: SubjectNamingStrategy
}

/// How Avro schema subjects are named in the Schema Registry.
enum SubjectNamingStrategy {
    /// Subject is derived from the Kafka topic name.
    ///
    /// Value subject: "{topic}-value"
    /// Key subject:   "{topic}-key"
    ///
    /// This is the Confluent Schema Registry default. Each topic has its own
    /// schema — the same Smithy shape used on two different topics registers
    /// as two independent subjects.
    TOPIC_NAME

    /// Subject is derived from the fully-qualified Avro record name.
    ///
    /// Value subject: "{avro.namespace}.{RecordName}"
    ///
    /// Allows the same schema to be reused across topics without registering
    /// it multiple times. Useful when a canonical event type appears on
    /// more than one topic.
    RECORD_NAME

    /// Subject combines topic name and Avro record name.
    ///
    /// Value subject: "{topic}-{avro.namespace}.{RecordName}"
    ///
    /// Provides topic-scoped uniqueness while still encoding the record type
    /// in the subject name.
    TOPIC_RECORD_NAME
}

/// Declares the Avro schema compatibility mode for an operation's topic.
///
/// Specifies which schema changes are permitted when a new version of the
/// payload shape is published. Smithy validators can use this annotation to
/// enforce that shape changes respect the declared compatibility contract —
/// catching breaking changes at model-build time rather than at runtime.
///
/// Applied at operation level to set the mode for a specific topic.
/// Applied at service level to set the default for all operations in the
/// service; an operation-level annotation takes precedence.
@trait(selector: ":is(service, operation)")
structure avroCompatibility {
    @required
    mode: CompatibilityMode
}

/// Avro schema compatibility modes as defined by the Confluent Schema Registry.
enum CompatibilityMode {
    /// No compatibility checks. Any schema change is permitted.
    ///
    /// Use only for topics where consumers are always updated in lockstep
    /// with producers, or during initial development.
    NONE

    /// New schema can read data written by the previous schema version.
    ///
    /// Safe change examples: adding an optional field, removing a field
    /// that has a default. Consumers can be upgraded before producers.
    BACKWARD

    /// New schema can read data written by all previous schema versions.
    ///
    /// Stricter form of BACKWARD: the new schema must be compatible with
    /// every prior version, not just the immediately preceding one.
    BACKWARD_TRANSITIVE

    /// Previous schema can read data written by the new schema version.
    ///
    /// Safe change examples: adding a field with a default, removing an
    /// optional field. Producers can be upgraded before consumers.
    FORWARD

    /// All previous schema versions can read data written by the new schema.
    ///
    /// Stricter form of FORWARD: every prior version must be able to read
    /// messages produced under the new schema.
    FORWARD_TRANSITIVE

    /// New schema is both BACKWARD and FORWARD compatible with the previous version.
    ///
    /// The most restrictive pairwise mode. Safe changes are limited to adding
    /// or removing fields that have defaults.
    FULL

    /// New schema is BACKWARD and FORWARD compatible with all previous versions.
    ///
    /// The most restrictive mode overall. Recommended for long-lived,
    /// widely-consumed topics where consumers at many different versions
    /// may be reading simultaneously.
    FULL_TRANSITIVE
}
