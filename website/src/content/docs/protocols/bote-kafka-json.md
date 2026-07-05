---
title: kafkaJson
description: JSON messages over Kafka via bote#kafkaJson, generated as a typed producer and consumers.
---

`bote#kafkaJson` is a JSON-over-Kafka protocol from the
[bote trait library](/smithy-dotnet/protocols/bote-overview/). NSmithy
generates a typed Kafka SDK from the service: a producer plus command and
event consumers over
[Confluent.Kafka](https://github.com/confluentinc/confluent-kafka-dotnet).
Status: **Experimental**.

See [Protocol Status](/smithy-dotnet/protocols/status/) for maturity details.

## Maven Dependency

```json
"io.github.thomaslaich.bote:bote:0.1.0-SNAPSHOT"
```

bote is not on Maven Central yet. Publish it to your local Maven repository
with `just publish-local` from the
[bote repo](https://github.com/thomaslaich/bote); the synthesized
`smithy-build.json` resolves `file://~/.m2/repository` when the contracts
declare it as a repository.

## NuGet Packages

| Purpose | Packages |
| --- | --- |
| Generated Kafka SDK | `NSmithy.Core`, `NSmithy.Codecs.Json`, `Confluent.Kafka` |
| AsyncAPI docs host | `NSmithy.Server.AspNetCore.Docs` |

## Modeling

Apply `@kafkaJson` to the service. Each operation is a capability carrying its
topic: `@kafkaProduce` takes a `@command` input and has no output;
`@kafkaConsume` outputs a `@streaming` union of `@event` structures.

```smithy
$version: "2"

namespace examples.kafka.streetlights

use bote#command
use bote#event
use bote#kafkaConsume
use bote#kafkaHeader
use bote#kafkaJson
use bote#kafkaKey
use bote#kafkaProduce

@kafkaJson
service StreetlightDevice {
    version: "1.0.0"
    operations: [ConsumeLightingEvents, DimLight]
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

@event
structure LightMeasured {
    /// Kafka message key. Routes one streetlight's events to one partition.
    @kafkaKey
    streetlightId: String

    @range(min: 0)
    lumens: Integer

    /// Carried as a Kafka header.
    @kafkaHeader(name: "my-app-id")
    appId: String

    sentAt: Timestamp
}

@command
structure DimLightCommand {
    @kafkaKey
    streetlightId: String

    @range(min: 0, max: 100)
    percentage: Integer

    sentAt: Timestamp
}

@streaming
union LightMeasuredStream {
    lightMeasured: LightMeasured
}
```

- `@kafkaKey` marks the member used as the Kafka message key.
- `@kafkaHeader(name: "...")` binds a member to a Kafka message header.
- Topic provisioning is not part of the contract. Attach
  `bote.infra#kafkaTopicConfig` (partitions, replication, retention) with
  `apply` from a separate model file.

## On the Wire

Message values are JSON. Timestamps default to `epoch-seconds`;
`@timestampFormat` overrides per member.

A **command** value is the bare JSON serialization of its structure. The
`@kafkaKey` member doubles as the Kafka message key:

```
topic:  smartylighting.streetlights.action.dim
key:    "streetlight-001"
value:  {"streetlightId":"streetlight-001","percentage":50,"sentAt":1751692800}
```

An **event** value is serialized according to the protocol's
`eventDiscrimination` setting, so consumers of a multi-event channel can tell
event types apart. The setting is part of the protocol contract: producer and
consumer must agree.

| Mode | Value | Discriminator |
| --- | --- | --- |
| `ENVELOPE` (default) | `{"lightMeasured": {...}}` | the `@streaming` union member name wraps the value, as in restJson1 tagged unions |
| `HEADER` | bare payload | a `bote-type` Kafka header carries the member name |
| `NONE` | bare payload | none; the channel carries a single event type (validator-enforced) |

```smithy
// Select a mode on the protocol trait; omit for ENVELOPE.
@kafkaJson(eventDiscrimination: "HEADER")
service StreetlightDevice { ... }
```

The protocol defines `@kafkaHeader` members to travel only as Kafka headers,
never inside the JSON value (see the limitation below for NSmithy's current
behavior).

## Generated Surfaces

From the `StreetlightDevice` service NSmithy generates one
`StreetlightDeviceKafka.g.cs`. The contract owner and its clients use
different halves of the same types:

- **`StreetlightDeviceProducer`**: the write side. One `{Op}Async(command)`
  method per `@kafkaProduce` (a client invoking a capability) and one
  `Publish{Event}Async(event)` method per `@kafkaConsume` union member (the
  owner emitting).
- **`IStreetlightDeviceCommandHandler`** / **`StreetlightDeviceCommandConsumer`**:
  the owner's command side. Consumes the `@kafkaProduce` topics and dispatches
  each command to `Handle{Op}Async`.
- **`IStreetlightDeviceEventHandler`** / **`StreetlightDeviceEventConsumer`**:
  a client's event side. Consumes the `@kafkaConsume` topics, decodes events
  per `eventDiscrimination`, and dispatches to `Handle{Event}Async`.

A dedicated operation input structure surfaces in C# as `{Op}Input`:
`DimLightCommand` above generates the C# type `DimLightInput`.

The owner (the streetlight device) handles commands and emits events:

```csharp
using Confluent.Kafka;
using Examples.Kafka.Streetlights;

await using var producer = new StreetlightDeviceProducer(
    new ProducerConfig { BootstrapServers = "localhost:9092" });
await using var commands = new StreetlightDeviceCommandConsumer(
    new ConsumerConfig { BootstrapServers = "localhost:9092", GroupId = "device" },
    new DimLightHandler());

var handling = commands.RunAsync(ct); // blocking consume loop on a background task

await producer.PublishLightMeasuredAsync(
    new LightMeasured(Lumens: 842, StreetlightId: "streetlight-001"), ct);

sealed class DimLightHandler : IStreetlightDeviceCommandHandler
{
    public Task HandleDimLightAsync(DimLightInput command, CancellationToken ct = default)
    {
        Console.WriteLine($"dim {command.StreetlightId} -> {command.Percentage}%");
        return Task.CompletedTask;
    }
}
```

A client (a controller) produces commands and consumes events:

```csharp
await using var producer = new StreetlightDeviceProducer(producerConfig);
await using var events = new StreetlightDeviceEventConsumer(
    consumerConfig, new LightMeasuredHandler());

var watching = events.RunAsync(ct);
await producer.DimLightAsync(
    new DimLightInput(Percentage: 50, StreetlightId: "streetlight-001"), ct);
```

Consumer group membership, offsets, and delivery semantics are runtime
concerns configured through Confluent's `ConsumerConfig` (`GroupId`,
`AutoOffsetReset`, and so on); they are not part of the model.

## AsyncAPI Documentation

Set `SmithyGenerateAsyncApi` in a project referencing the contracts to run
bote's AsyncAPI 3.1 generator and copy the document to `wwwroot/asyncapi.json`,
then serve it with Scalar:

```xml
<PropertyGroup>
  <SmithyService>examples.kafka.streetlights#StreetlightDevice</SmithyService>
  <SmithyGenerateAsyncApi>true</SmithyGenerateAsyncApi>
</PropertyGroup>
```

```csharp
using NSmithy.Server.AspNetCore.Docs;

var app = builder.Build();
app.MapSmithyAsyncApi(); // /asyncapi.json + Scalar at /asyncapi
app.Run();
```

See the runnable
[`examples/kafka`](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/kafka)
project for the full setup, including a Docker Compose Kafka broker.

## Current Limitations

- `@kafkaHeader` members are serialized into the JSON value in addition to the
  Kafka header; the protocol specifies headers only.
- `eventDiscrimination` `HEADER` and `NONE` are implemented in the generator
  but only `ENVELOPE` is exercised by the example.
- No dependency-injection or hosted-service registration; consumers are driven
  manually via `RunAsync`, and handlers are constructed by the caller.
- The consume loop swallows non-fatal `ConsumeException`s; there is no retry,
  dead-letter, or error callback mechanism.
- `bote#kafkaAvro` and `bote#kafkaProtobuf` are defined by bote but have no
  NSmithy generator.
- smithy-docgen (Sphinx) cannot be enabled for bote services: it rejects
  `@command` structures used as operation inputs without `@input`. Use the
  AsyncAPI document instead.
- No conformance suite; behavior is validated through the end-to-end example.
