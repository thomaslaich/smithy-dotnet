# Kafka JSON example — Streetlight Device

Demonstrates generating a typed Kafka SDK from a Smithy model using the
[bote](https://github.com/thomaslaich/bote) `@kafkaJson` protocol.

The contract (`device.contracts/model/streetlights.smithy`) is **owned by the device**:
it defines the events the device emits and the commands it accepts. Two roles
share the one contract:

- the **device** (owner) emits `LightMeasured` events and handles `DimLight` commands
- the **controller** (client) produces `DimLight` commands and consumes `LightMeasured` events

## What gets generated

From the `StreetlightDevice` service, the `NSmithy.Bote` extension generates
`Examples/Kafka/Streetlights/StreetlightDeviceKafka.g.cs` containing a complete,
role-neutral SDK:

**`StreetlightDeviceProducer`** (`IAsyncDisposable`) — the write side:
```csharp
// @kafkaProduce — a client writes a bare @command to the command topic
await producer.DimLightAsync(new DimLightInput(StreetlightId: "...", Percentage: 50));

// @kafkaConsume — the owner emits an @event (union-wrapped) to the event topic
await producer.PublishLightMeasuredAsync(new LightMeasured(streetlightId: "...", lumens: 800));
```

**`IStreetlightDeviceCommandHandler`** / **`StreetlightDeviceCommandConsumer`** — the
owner's command side: consumes the `@kafkaProduce` topics, deserializes the bare
command, dispatches to `Handle{Op}Async`.

**`IStreetlightDeviceEventHandler`** / **`StreetlightDeviceEventConsumer`** — the
client's event side: consumes the `@kafkaConsume` topics, deserializes the
`@streaming` union, dispatches each variant to `Handle{Event}Async`.

```csharp
await using var events = new StreetlightDeviceEventConsumer(config, new MyEventHandler());
await events.RunAsync(cancellationToken);
```

Commands are written as the bare payload (one command type per topic). Event
serialization follows the protocol's `eventDiscrimination` setting — the default
`ENVELOPE` writes events union-wrapped (`{"lightMeasured": {...}}`) so one event
topic can carry several event types, distinguished by the union member name;
`HEADER` writes the bare payload plus a `bote-type` Kafka header; `NONE` writes
the bare payload on single-event channels. `@kafkaKey` members become the
Confluent.Kafka message key; `@kafkaHeader` members become headers.

With `SmithyGenerateDependencyInjection=true` (the device project), NSmithy.Bote also
generates **Microsoft.Extensions hosting registrations**:
`AddStreetlightDeviceProducer(config)` registers the producer as a singleton, and
`AddStreetlightDeviceCommandConsumer(config)` / `AddStreetlightDeviceEventConsumer(config)`
run the consumers as `BackgroundService`s. The registered handler is resolved in
a new service scope per message. The device runs as a generic host this way;
the controller drives the same SDK manually to show both usages.

## Model overview

```
device.contracts/model/streetlights.smithy   (namespace examples.kafka.streetlights)
device.infra/model/topics.smithy             (namespace examples.kafka.infra)
```

| Operation               | Trait            | Topic                                            | Payload        |
|-------------------------|------------------|--------------------------------------------------|----------------|
| `DimLight`              | `@kafkaProduce`  | `smartylighting.streetlights.action.dim`         | `DimLightCommand` (`@command`) |
| `ConsumeLightingEvents` | `@kafkaConsume`  | `smartylighting.streetlights.lighting.measured`  | `LightMeasured` (`@event`) via `LightMeasuredStream` |

The topic is carried by the `@kafkaProduce` / `@kafkaConsume` trait itself; topic
provisioning (`bote.infra#kafkaTopicConfig`) is applied separately in
`device.infra/model/topics.smithy`. Both artifacts belong to the device team,
while their separate projects let the portable application contract and
environment-specific deployment configuration evolve independently.

NSmithy generates `StreetlightDeviceKafkaInfrastructure.Topics` from the
infrastructure overlay. The `device.infra` console reconciles that typed desired
state through Confluent's Admin API. The `device.docs` project independently
composes the same models into an AsyncAPI 3.1 document for platform-neutral
documentation and tooling.

## Prerequisites

- .NET 10 SDK
- Docker (for the local Kafka broker)
- NSmithy packages built locally (`just pack` from the repo root)
- `NSmithy.Bote`, packed with the other NSmithy packages into
  `artifacts/packages`

`NSmithy.Bote` bundles the Bote model and code generators; Maven and a separate
JRE are not required.

## Running the example

**1. Deploy the device infrastructure**
```sh
docker compose up -d kafka
dotnet run --project device.infra
```

The C# infrastructure deployer creates missing topics, increases partition
counts when needed, and applies topic configuration. Kafka does not support
reducing a partition count; replication-factor changes require reassignment, so
the deployer reports those cases instead of hiding them. Run the deployer
directly against another broker with
`dotnet run --project device.infra -- broker:9092`.

**2. Start the device** (in one terminal) — emits events, handles commands:
```sh
dotnet run --project device
```

**3. Run the controller** (in another terminal) — dims the light, watches events:
```sh
dotnet run --project controller
```

You should see the device receive the dim commands and the controller receive the
device's lighting measurements:
```
[device] DimLight received  streetlight=streetlight-001 -> 50%
[controller] LightMeasured  streetlight=streetlight-001 lumens=363 at=21:17:51
```

**4. Stop** — Ctrl+C in each terminal, then `docker compose down`.

## Viewing the AsyncAPI document

The `device.docs` project serves the generated AsyncAPI 3.1 document and renders it with
[Scalar](https://scalar.com/products/api-references/asyncapi). It opts in with a
single MSBuild flag (the AsyncAPI analogue of `SmithyOpenApiProtocol`):

```xml
<SmithyGenerateAsyncApi>true</SmithyGenerateAsyncApi>
```

which injects bote's `asyncapi` plugin and copies the document to
`wwwroot/asyncapi.json`. Run it:

```sh
dotnet run --project device.docs
```

Then open the printed URL — `/asyncapi.json` serves the raw document and `/`
renders it in Scalar. (Scalar's AsyncAPI rendering is still early/limited; the
document itself is plain AsyncAPI 3.1 and also opens in
[AsyncAPI Studio](https://studio.asyncapi.com).)
