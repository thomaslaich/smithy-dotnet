$version: "2"

// Topic provisioning owned by the streetlight device team. The application
// contract stays portable while this overlay captures deployable Kafka settings.
namespace examples.kafka.infra

use bote.infra#kafkaTopicConfig

apply examples.kafka.streetlights#ConsumeLightingEvents @kafkaTopicConfig(
    partitions: 3
    replicationFactor: 1
    retentionMs: 604800000 // 7 days
)

apply examples.kafka.streetlights#DimLight @kafkaTopicConfig(
    partitions: 3
    replicationFactor: 1
    retentionMs: 86400000 // 1 day
)
