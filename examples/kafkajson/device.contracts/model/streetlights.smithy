$version: "2"

// The streetlight device contract. The device/product owns the messages because
// it defines the events it emits and the commands it accepts.
namespace examples.kafka.streetlights

use bote#command
use bote#event
use bote#kafkaConsume
use bote#kafkaHeader
use bote#kafkaJson
use bote#kafkaKey
use bote#kafkaProduce

/// The streetlight device API: clients consume lighting events and produce dim commands.
@title("Streetlight Device API")
@kafkaJson
service StreetlightDevice {
    version: "1.0.0"
    operations: [
        ConsumeLightingEvents
        DimLight
    ]
}

/// Consume environmental lighting events reported by streetlights.
@kafkaConsume(topic: "smartylighting.streetlights.lighting.measured")
operation ConsumeLightingEvents {
    output := {
        events: LightMeasuredStream
    }
}

/// Dim a streetlight.
@kafkaProduce(topic: "smartylighting.streetlights.action.dim")
operation DimLight {
    input: DimLightCommand
}

/// Light intensity reported by a streetlight.
@event
structure LightMeasured {
    /// Routes all measurements for one streetlight to the same partition.
    @kafkaKey
    streetlightId: String

    /// Light intensity measured in lumens.
    @range(min: 0)
    lumens: Integer

    /// Identifies the producing application; carried as a Kafka header.
    @kafkaHeader(name: "my-app-id")
    appId: String

    /// Date and time when the message was sent.
    sentAt: Timestamp
}

/// Command a particular streetlight to dim the lights.
@command
structure DimLightCommand {
    @kafkaKey
    streetlightId: String

    /// Percentage to which the light should be dimmed to.
    @range(min: 0, max: 100)
    percentage: Integer

    /// Date and time when the message was sent.
    sentAt: Timestamp
}

/// The client's subscription view of device lighting events.
@streaming
union LightMeasuredStream {
    lightMeasured: LightMeasured
}
