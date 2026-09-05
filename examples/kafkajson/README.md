# Kafka JSON example

This example generates a typed Kafka SDK for a streetlight device using the
[bote](https://github.com/thomaslaich/bote) `@kafkaJson` protocol.

The device owns the contract: it defines the `LightMeasured` events it emits
and the `DimLight` commands it accepts. A controller acts as a client by sending
commands and consuming events.

## Projects

- `device.contracts`: the portable Smithy messaging contract.
- `device`: the owner application; it emits events and handles commands.
- `controller`: a client; it sends commands and handles events.
- `device.infra`: the deployment overlay and topic reconciler.
- `device.docs`: generated AsyncAPI documentation.

## Generated code

`NSmithy.Bote` generates contract-specific bindings and service-role interfaces:

- `IStreetlightDeviceClient` sends `DimLightAsync` commands.
- `IStreetlightDeviceEventPublisher` publishes `PublishLightMeasuredAsync` events.
- `IDimLightHandler.HandleAsync(DimLightInput, CancellationToken)` handles commands.
- `IConsumeLightingEventsHandler.HandleAsync(LightMeasuredStream, CancellationToken)`
  handles the modeled event union.

`NSmithy.Messaging.Kafka` owns the Kafka producer, polling loop, offset storage,
and hosted service. Generated code contains no broker loops. A new DI scope is
created for each delivery and asynchronously disposed before offset storage.

The consumer processes messages sequentially. Decode or handler failure stops it
without advancing the failed delivery; restarting can replay messages, so handlers
must tolerate duplicates. Unknown event discriminators fail decoding. Shutdown
cancels processing, waits for the active handler, and closes the Kafka consumer.
Batch processing, automatic retry, and dead-letter policies are not implemented.

Commands use a bare JSON structure, with one command type per topic. Events use
the protocol's `eventDiscrimination` setting:

- `ENVELOPE` (default) wraps the event in its union member name, for example
  `{"lightMeasured": {...}}`.
- `HEADER` writes a bare payload and carries the union member name in a
  `bote-type` Kafka header.
- `NONE` writes a bare payload on a single-event channel.

Members with `@kafkaKey` become the Kafka message key. Members with
`@kafkaHeader` travel only in Kafka headers: generated producers omit them from
the JSON value, and generated consumers restore them before calling the handler.

Both applications enable `SmithyGenerateDependencyInjection`. Configure the broker
once with `AddKafkaMessaging(new KafkaMessagingOptions { Producer = ..., Consumer = ... })`.
The device calls `AddStreetlightDeviceEventPublisher()` and
`AddStreetlightDeviceCommandConsumer()`, then registers `IDimLightHandler`.
The controller calls `AddStreetlightDeviceClient()` and
`AddStreetlightDeviceEventConsumer()`, then registers `IConsumeLightingEventsHandler`.
Missing handlers fail startup before the consumer connects to Kafka.

## Model and infrastructure

```text
device.contracts/model/streetlights.smithy   (namespace examples.kafka.streetlights)
device.infra/model/topics.smithy             (namespace examples.kafka.infra)
```

| Operation | Trait | Topic | Payload |
| --- | --- | --- | --- |
| `DimLight` | `@kafkaProduce` | `smartylighting.streetlights.action.dim` | `DimLightCommand` (`@command`) |
| `ConsumeLightingEvents` | `@kafkaConsume` | `smartylighting.streetlights.lighting.measured` | `LightMeasured` (`@event`) through `LightMeasuredStream` |

The capability traits carry topic names. The separate
`bote.infra#kafkaTopicConfig` overlay adds deployable topic settings without
putting environment-specific configuration in the portable contract.

NSmithy generates `StreetlightDeviceKafkaInfrastructure.Topics` from that
overlay. The `device.infra` console reconciles the typed desired state through
Confluent's Admin API; it does not deploy a Kafka cluster. The `device.docs`
project composes the same models into an AsyncAPI 3.1 document.

## Prerequisites

- .NET 10 SDK
- `just`, or the repository toolchain through `devenv shell`
- Docker

`NSmithy.Bote` bundles the Bote model and code generators, so consumers do not
need Maven or a separate JRE.

## Build

Run all commands in this README from `examples/kafkajson`. First build the local
packages and examples:

```bash
just build
just pack
just refresh-examples
```

## Run

### 1. Start Kafka and initialize the topics

```bash
docker compose up -d kafka
dotnet run --project device.infra
```

The reconciler creates missing topics, increases partition counts when needed,
and applies topic configuration. Kafka cannot reduce a partition count, and
replication-factor changes require reassignment, so the reconciler reports
those cases. To use another broker:

```bash
dotnet run --project device.infra -- broker:9092
```

### 2. Start the device

In one terminal, start the owner that emits events and handles commands:

```bash
dotnet run --project device
```

### 3. Run the controller

In another terminal, start the client that dims the light and watches events:

```bash
dotnet run --project controller
```

Expected output includes:

```text
[device] DimLight received  streetlight=streetlight-001 -> 50%
[controller] LightMeasured  streetlight=streetlight-001 lumens=363 at=21:17:51
```

### 4. Stop

Stop each application with Ctrl+C, then remove Kafka:

```bash
docker compose down
```

## View the AsyncAPI document

The `device.docs` project serves the generated AsyncAPI 3.1 document and renders
it with [Scalar](https://scalar.com/products/api-references/asyncapi). It opts in
with the AsyncAPI analogue of `SmithyOpenApiProtocol`:

```xml
<SmithyGenerateAsyncApi>true</SmithyGenerateAsyncApi>
```

This setting injects bote's `asyncapi` plugin and copies the document to
`wwwroot/asyncapi.json`. Run it with:

```bash
dotnet run --project device.docs
```

The raw document is available at `/asyncapi.json`, and `/` renders it in Scalar.
Scalar's AsyncAPI rendering remains limited; the document is standard AsyncAPI
3.1 and also opens in [AsyncAPI Studio](https://studio.asyncapi.com/).
