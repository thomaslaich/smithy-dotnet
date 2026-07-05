$version: "2"

// Topic provisioning for the streetlights contract. Deliberately separate from
// streetlights.smithy: the device owns its commands and events, while a
// platform team owns partitions, replication, and retention.
// bote.infra#kafkaTopicConfig is attached with apply so the two concerns can
// be authored — and owned — independently.
namespace examples.kafka.infra

use bote.infra#kafkaTopicConfig

apply examples.kafka.streetlights#ConsumeLightingEvents @kafkaTopicConfig(
    partitions: 3
    replicationFactor: 1
    retentionMs: 604800000 // 7 days
)
