# Kafka example — Streetlight Device

Demonstrates generating a typed Kafka SDK from a Smithy model using the
[bote](https://github.com/thomaslaich/bote) `@kafkaJson` protocol.

The contract (`contracts/model/streetlights.smithy`) is **owned by the device**:
it defines the events the device emits and the commands it accepts. Two roles
share the one contract:

- the **device** (owner) emits `LightMeasured` events and handles `DimLight` commands
- the **controller** (client) produces `DimLight` commands and consumes `LightMeasured` events

## What gets generated

From the `StreetlightDevice` service, NSmithy generates
`Examples/Kafka/Streetlights/StreetlightDeviceKafka.g.cs` containing a complete,
role-neutral SDK:

**`StreetlightDeviceProducer`** (`IAsyncDisposable`) — the write side:
```csharp
// @kafkaProduce — a client writes a bare @command to the command topic
await producer.DimLightAsync(new DimLightCommand(streetlightId: "...", percentage: 50));

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

With `SmithyGenerateDependencyInjection=true` (the device project), NSmithy also
generates **Microsoft.Extensions hosting registrations**:
`AddStreetlightDeviceProducer(config)` registers the producer as a singleton, and
`AddStreetlightDeviceCommandConsumer(config)` / `AddStreetlightDeviceEventConsumer(config)`
run the consumers as `BackgroundService`s. The registered handler is resolved in
a new service scope per message. The device runs as a generic host this way;
the controller drives the same SDK manually to show both usages.

## Model overview

```
contracts/model/streetlights.smithy   (namespace examples.kafka.streetlights)
```

| Operation               | Trait            | Topic                                            | Payload        |
|-------------------------|------------------|--------------------------------------------------|----------------|
| `DimLight`              | `@kafkaProduce`  | `smartylighting.streetlights.action.dim`         | `DimLightCommand` (`@command`) |
| `ConsumeLightingEvents` | `@kafkaConsume`  | `smartylighting.streetlights.lighting.measured`  | `LightMeasured` (`@event`) via `LightMeasuredStream` |

The topic is carried by the `@kafkaProduce` / `@kafkaConsume` trait itself; topic
provisioning (`bote.infra#kafkaTopicConfig`) is applied separately in
`contracts/model/infra.smithy`, so contract and infrastructure can be owned
independently.

The contracts project also runs bote's **AsyncAPI** generator; the AsyncAPI 3.1
document is written to
`contracts/obj/Debug/net10.0/Smithy/source/asyncapi/StreetlightDevice.asyncapi.json`.

## Prerequisites

- .NET 10 SDK
- Docker (for the local Kafka broker)
- NSmithy packages built locally (`just pack` from the repo root)
- bote + smithy-asyncapi JARs published to local Maven (`~/.m2`) from the
  [bote repo](https://github.com/thomaslaich/bote) (`just publish-local`)

## Running the example

**1. Start Kafka**
```sh
docker compose up -d
```

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

The `host` project serves the generated AsyncAPI 3.1 document and renders it with
[Scalar](https://scalar.com/products/api-references/asyncapi). It opts in with a
single MSBuild flag (the AsyncAPI analogue of `SmithyOpenApiProtocol`):

```xml
<SmithyGenerateAsyncApi>true</SmithyGenerateAsyncApi>
```

which injects bote's `asyncapi` plugin and copies the document to
`wwwroot/asyncapi.json`. Run it:

```sh
dotnet run --project host
```

Then open the printed URL — `/asyncapi.json` serves the raw document and `/`
renders it in Scalar. (Scalar's AsyncAPI rendering is still early/limited; the
document itself is plain AsyncAPI 3.1 and also opens in
[AsyncAPI Studio](https://studio.asyncapi.com).)
