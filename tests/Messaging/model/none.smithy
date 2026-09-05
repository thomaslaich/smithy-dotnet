$version: "2"
namespace tests.none
use bote#command
use bote#event
use bote#kafkaConsume
use bote#kafkaProduce
use bote#kafkaHeader
use bote#kafkaKey
use bote#kafkaJson
@kafkaJson(eventDiscrimination: "NONE")
service Device { version: "1", operations: [Dim, Watch] }
@kafkaProduce(topic: "none.commands")
operation Dim { input: DimCommand }
@command
structure DimCommand {
    @required
    @kafkaKey
    deviceId: String
    @required
    @kafkaHeader(name: "trace-id")
    traceId: String
}
@kafkaConsume(topic: "none.events")
operation Watch { output := { events: DeviceEvents } }
@streaming
union DeviceEvents {
    @jsonName("measured-event")
    measured: Measured
}
@event
structure Measured {
    @required
    @kafkaHeader(name: "source")
    source: String
    @required
    lumens: Integer
}
