---
title: Kafka JSON
description: JSON messages over Kafka via bote#kafkaJson, generated as a typed producer and consumers.
---

`bote#kafkaJson` is a JSON-over-Kafka protocol from the
[bote trait library](/smithy-dotnet/protocols/bote-overview/). The
`NSmithy.Bote` generates a typed Kafka SDK from the service: a producer plus command and
event consumers over
[Confluent.Kafka](https://github.com/confluentinc/confluent-kafka-dotnet).
Status: **Experimental**.

See [Protocol Status](/smithy-dotnet/protocols/status/) for maturity details.

## Maven Dependency

```json
"io.github.thomaslaich.bote:bote:0.1.0"
```

The trait library is published on Maven Central. `NSmithy.Bote` also bundles
it hermetically for generated .NET builds.

## NuGet Packages

| Purpose | Packages |
| --- | --- |
| Build integration | `NSmithy.Bote` |
| Generated Kafka SDK | `NSmithy.Core`, `NSmithy.Codecs.Json`, `NSmithy.Messaging.Kafka` |
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
- `@kafkaHeader(name: "...")` binds a simple or blob member to a Kafka message
  header. Producers omit it from JSON, and consumers hydrate it from the last
  header with that name before constructing the message.
- Topic provisioning is not part of the contract. Attach
  `bote.infra#kafkaTopicConfig` (partitions, replication, retention) with
  `apply` from a separately deployable infrastructure model owned by the
  service team. NSmithy generates a typed `{Service}KafkaInfrastructure.Topics`
  deployment plan from the composed models. A console deployer, an Aspire
  AppHost integration, or organization-specific infrastructure tooling can
  consume the same plan; AsyncAPI remains the platform-neutral export.

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

`@kafkaHeader` members travel only as Kafka headers, never inside the JSON
value. Strings and other simple values use UTF-8 text with invariant formatting;
blobs use their bytes directly. A missing required header fails deserialization
like any other missing required member.

## Generated Surfaces

The generated API separates service roles:

- `IStreetlightDeviceClient` / `StreetlightDeviceClient` sends commands through
  `DimLightAsync`.
- `IStreetlightDeviceEventPublisher` / `StreetlightDeviceEventPublisher` publishes
  events through `PublishLightMeasuredAsync`.
- `IDimLightHandler.HandleAsync(DimLightInput, CancellationToken)` handles the
  owner's command operation.
- `IConsumeLightingEventsHandler.HandleAsync(LightMeasuredStream, CancellationToken)`
  handles the client's event operation as a modeled union. Discrimination remains
  inside the generated binding.

A dedicated operation input structure surfaces in C# as `{Op}Input`:
`DimLightCommand` above generates the C# type `DimLightInput`.

Clients and publishers delegate to `IMessageSender` through immutable operation
bindings. Bindings contain service/operation IDs, addresses, codecs, keys,
headers, and typed dispatch. `NSmithy.Messaging` provides a fresh DI scope for each
delivery. `NSmithy.Messaging.Kafka` owns broker connections, polling, offset
storage, and hosting. Only composition-root configuration uses Kafka SDK types.

The first runtime processes deliveries sequentially. It enables automatic
commits and disables automatic offset storage, storing a position only after
successful dispatch and asynchronous scope disposal. Decode or handler failure
stops the consumer without advancing that delivery. Unknown event discriminators
are decode failures. Restarting can replay messages; handlers must be idempotent
where duplicates matter. Lost partition ownership is logged and allows the new
owner to replay. Shutdown closes the consumer after processing exits.

Configure `ConsumerConfig.GroupId` and `AutoOffsetReset` at the composition root.
Keep processing time within Kafka's configured `MaxPollIntervalMs`; long handlers
can lose partition ownership. Automatic retry, dead-letter handling, concurrency,
and batch APIs are not implemented in this first runtime slice.

## Kafka Infrastructure Generation

Keep deployable topic settings in an infrastructure overlay rather than in the
portable application contract. The overlay applies Bote's
`bote.infra#kafkaTopicConfig` trait to the operations that own the topics:

```smithy
$version: "2"

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
```

An infrastructure project composes that local overlay with the contract model.
It can disable the client and server surfaces because it needs only the generated
deployment plan:

```xml
<PropertyGroup>
  <SmithyService>examples.kafka.streetlights#StreetlightDevice</SmithyService>
  <SmithyGenerateClient>false</SmithyGenerateClient>
  <SmithyGenerateServer>false</SmithyGenerateServer>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="NSmithy.Bote" Version="..." PrivateAssets="all" />
  <ProjectReference
    Include="../device.contracts/Device.Contracts.csproj"
    ReferenceOutputAssembly="false"
  />
</ItemGroup>
```

NSmithy.Bote generates a provider-neutral C# description of the desired topics:

```csharp
public static class StreetlightDeviceKafkaInfrastructure
{
    public sealed record Topic(
        string Name,
        int? Partitions,
        short? ReplicationFactor,
        IReadOnlyDictionary<string, string> Configuration);

    public static IReadOnlyList<Topic> Topics { get; } = [/* modeled topics */];
}
```

The generated surface deliberately describes desired state; it does not perform
deployment. A small adapter can reconcile it through Confluent's Admin API,
translate it into an organization's infrastructure system, or expose it to an
Aspire AppHost. This keeps the Smithy codegen independent of both Confluent's
deployment policy and Aspire. NSmithy.Bote does not currently ship an Aspire
adapter.

The runnable example's `device.infra` console is an idempotent Confluent adapter:
it creates missing topics, increases partition counts, and applies topic
configuration. Kafka cannot reduce partition counts, and replication-factor
changes require reassignment, so those cases are reported rather than silently
ignored.

## Dependency Injection

Setting `SmithyGenerateDependencyInjection` to `true` generates registration
helpers. Add `NSmithy.Messaging.Kafka` to the application and configure the broker
once before registering the generated service roles:

```csharp
using Confluent.Kafka;
using Examples.Kafka.Streetlights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Messaging.Kafka;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddKafkaMessaging(new KafkaMessagingOptions
{
    Producer = new ProducerConfig { BootstrapServers = "localhost:9092" },
    Consumer = new ConsumerConfig
    {
        BootstrapServers = "localhost:9092",
        GroupId = "device",
        AutoOffsetReset = AutoOffsetReset.Earliest,
    },
});
builder.Services.AddStreetlightDeviceEventPublisher();
builder.Services.AddStreetlightDeviceCommandConsumer();
builder.Services.AddScoped<IDimLightHandler, DimLightHandler>();
await builder.Build().RunAsync();

sealed class DimLightHandler : IDimLightHandler
{
    public Task HandleAsync(DimLightInput command, CancellationToken ct = default)
    {
        Console.WriteLine($"dim {command.StreetlightId} -> {command.Percentage}%");
        return Task.CompletedTask;
    }
}
```

The controller instead registers `AddStreetlightDeviceClient()`,
`AddStreetlightDeviceEventConsumer()`, and its `IConsumeLightingEventsHandler`.
Inject `IStreetlightDeviceClient` or `IStreetlightDeviceEventPublisher` into
application services. The container owns the shared Kafka sender's lifetime.
For direct sending, construct `KafkaMessageSender` and pass it to the generated
client or publisher; the caller then owns sender disposal.

Each consumed operation requires a handler registration before startup. A handler
exception stops the worker; by default the .NET host then shuts down. The runtime
never acknowledges a failed handler merely to continue consuming.

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

See the runnable [Kafka JSON example](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/kafkajson)
for separate `device.contracts` and `device.infra` projects, model-driven topic
deployment through Confluent's Admin API, generated clients, and AsyncAPI
documentation. Aspire can host the broker and invoke the same infrastructure
project without becoming a dependency of the generated model.

## Current Limitations

- The runnable example exercises `ENVELOPE`; `HEADER` and `NONE` are covered by
  generator tests.
- The consume loop swallows non-fatal `ConsumeException`s; there is no retry,
  dead-letter, or error callback mechanism.
- `bote#kafkaAvro` and `bote#kafkaProtobuf` are defined by bote but have no
  NSmithy generator.
- smithy-docgen (Sphinx) cannot be enabled for bote services: it rejects
  `@command` structures used as operation inputs without `@input`. Use the
  AsyncAPI document instead.
- No conformance suite; behavior is validated through the end-to-end example.
