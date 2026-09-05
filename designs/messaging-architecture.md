# Messaging Architecture

Target architecture for generated Bote clients, handlers, operation bindings,
and broker runtimes.

## Status

Kafka and Redis now generate immutable send/receive bindings, service-role clients and
publishers, and per-operation handlers. `NSmithy.Messaging` owns scoped dispatch;
`NSmithy.Messaging.Kafka` owns producer lifetime, polling, offset storage, and
hosted consumption. `NSmithy.Messaging.Redis` owns stream recovery, checkpoints,
request/reply, acknowledgment, and Pub/Sub subscription lifetime. Generated code
has no broker loops or SDK dependency
outside composition-root registration and optional infrastructure tooling.

The runtimes process one message at a time. Decode and handler failures
stop consumption without settling the failed delivery. Missing handlers fail
startup. Unknown Kafka event discriminators are decode failures. Retries, dead-letter
policies, concurrency, batch APIs, and common telemetry remain future work.

Redis Streams uses consumer groups by default and recovers idle pending entries
with `XAUTOCLAIM` (Redis 6.2+). Independent `XREAD` processing uses the same event
handlers and can persist progress through `IRedisStreamCheckpointStore`.
`RedisStreamCheckpointStore` supplies Redis-backed persistence. Request/reply
handlers return typed replies; scoped dispatch encodes them, and the transport
publishes each reply before acknowledgment. Pub/Sub uses a bounded dispatch
queue and stops on handler failure or overflow; it cannot redeliver messages.
Broker subscription methods no longer expose `IAsyncEnumerable<T>`.


## Goal

Application code should depend on the modeled service, not on Kafka, Redis, or
AMQP APIs. Changing the broker protocol may require regenerating the service and
changing composition-root configuration, but it should not require rewriting
business handlers.

The central boundary is the same one used by the HTTP stack:

> Generated code contains contract-specific facts. Runtime libraries contain
> behavior and state machines.

For messaging this means:

- generated types describe messages, operations, addresses, codecs, keys,
  headers, and typed dispatch;
- generated public interfaces describe sending, publishing, and handling typed
  messages;
- reusable transport runtimes own polling, batching, concurrency, retries,
  delivery settlement, and hosting;
- broker SDK types appear only in transport configuration and runtime packages.

Protocol neutrality does not mean pretending that every broker has identical
semantics. Kafka offsets, Redis pending entries, and AMQP delivery tags remain
inside their respective runtimes. They meet at the typed handler boundary.

## Service Roles

A Bote service is modeled from its owner's perspective:

- clients send commands that the owner handles;
- the owner publishes events that clients handle;
- a command may produce a reply when the protocol supports request/reply.

The generated surface should keep those roles distinct. A single transport
producer object is not a useful contract abstraction because it combines a
client sending commands with an owner publishing events.

A simplified public surface is:

```csharp
public interface IStreetlightDeviceClient
{
    Task DimLightAsync(
        DimLightCommand command,
        CancellationToken cancellationToken = default);

    Task DimLightBatchAsync(
        IReadOnlyList<DimLightCommand> commands,
        CancellationToken cancellationToken = default);
}

public interface IStreetlightDeviceEventPublisher
{
    Task PublishLightMeasuredAsync(
        LightMeasured message,
        CancellationToken cancellationToken = default);

    Task PublishLightMeasuredBatchAsync(
        IReadOnlyList<LightMeasured> messages,
        CancellationToken cancellationToken = default);
}
```

Both interfaces are protocol-neutral. Their implementations delegate to the
configured messaging runtime.

## Handler Surface

Reliable broker consumption is represented by handlers, not by asynchronous
enumerables. Each consumed operation has single-message and batch alternatives:

```csharp
public interface IDimLightHandler
{
    Task HandleAsync(
        DimLightCommand command,
        CancellationToken cancellationToken = default);
}

public interface IDimLightBatchHandler
{
    Task HandleBatchAsync(
        IReadOnlyList<DimLightCommand> commands,
        CancellationToken cancellationToken = default);
}
```

An event operation handles its modeled streaming union rather than generating a
method per union member:

```csharp
public interface IConsumeLightingEventsHandler
{
    Task HandleAsync(
        LightMeasuredStream message,
        CancellationToken cancellationToken = default);
}

public interface IConsumeLightingEventsBatchHandler
{
    Task HandleBatchAsync(
        IReadOnlyList<LightMeasuredStream> messages,
        CancellationToken cancellationToken = default);
}
```

Using the union preserves heterogeneous event order and keeps the handler tied
to the Smithy operation rather than to one protocol's discrimination mechanism.
Generated aggregate interfaces may group operation handlers for convenience,
but per-operation interfaces are the primary registration boundary. This lets
one service use single-message handling for one operation and batch handling for
another.

Exactly one handler mode is selected per consumed operation. Registering both,
or neither when the consumer is enabled, is a startup error.

### Request/reply batches

For a command with a reply, the batch handler returns replies in input order:

```csharp
public interface IGetStockBatchHandler
{
    Task<IReadOnlyList<GetStockReply>> HandleBatchAsync(
        IReadOnlyList<GetStockRequest> requests,
        CancellationToken cancellationToken = default);
}
```

The result count must equal the input count. The runtime correlates and
publishes each reply before settling the input deliveries. A failure after some
replies have been published can produce duplicate replies on redelivery;
exactly-once request/reply is not promised.

## Why Brokers Do Not Expose `IAsyncEnumerable<T>`

An `IAsyncEnumerable<T>` models a caller pulling values through an in-process
lifetime. It does not provide a natural place for durable delivery settlement:

- advancing before processing gives at-most-once behavior;
- advancing after `yield return` cannot tell whether the caller completed
  processing successfully;
- yielding a bare model value hides Kafka offsets, Redis entry IDs, and AMQP
  delivery tags;
- exposing those transport tokens would make application code protocol-specific;
- cancellation and iterator disposal do not express acknowledgment or retry
  policy precisely.

Consequently no broker protocol, including Redis Streams and Redis Pub/Sub,
should generate subscription methods returning `IAsyncEnumerable<T>`.

This does not apply to HTTP or gRPC event streams. Those streams are scoped to a
request/connection and do not claim durable broker settlement. Their modeled
`IAsyncEnumerable<TEvent>` surface remains appropriate.

Redis Streams supports two native reading styles, but both fit without exposing
an enumerable:

- reliable application processing uses `XREADGROUP`, pending-entry recovery,
  and `XACK` behind handlers;
- independent cursor-based processing uses `XREAD`, invokes the same handlers,
  and persists the last successfully handled entry ID through a configured
  checkpoint store.

`XREADGROUP` is the default for scalable application processing: each logical
subscriber has a group, and its replicas share the work. `XREAD` is useful when
each reader must independently observe every entry. A durable `XREAD` reader
loads its cursor at startup and saves it only after handler success; without a
durable checkpoint store its progress survives only for the current process.
The cursor remains runtime state and is never passed to the handler.

Redis Pub/Sub also uses handlers. Because Pub/Sub cannot replay or acknowledge,
handler failure is observable but cannot cause broker redelivery. Its delivery
guarantee remains at-most-once even though its programming interface matches the
other brokers.

## Generated Operation Bindings

Generated code describes each operation with an immutable binding. A binding
contains only service-specific facts:

```csharp
internal static class StreetlightDeviceMessaging
{
    internal static readonly MessageSendBinding<DimLightCommand> DimLight =
        new(
            serviceId: StreetlightDeviceSchema.Id,
            operationId: DimLightSchema.Id,
            address: "smartylighting.streetlights.action.dim",
            serialize: SerializeDimLight,
            deserialize: DeserializeDimLight,
            metadata: command => new MessageMetadata(
                key: command.StreetlightId,
                headers: []));
}
```

Bindings may contain generated delegates for:

- payload serialization and deserialization;
- event-union discrimination;
- partition-key extraction;
- modeled header extraction and hydration;
- typed handler dispatch;
- reply correlation.

They do not contain polling loops, timers, retry loops, mutable broker clients,
or settlement policy.

The protocol binder may compile these delegates from the generated schemas at
client or host construction, as HTTP protocols already do. They are reused for
the lifetime of the client or consumer.

## Runtime Layers

The messaging stack has three runtime responsibilities:

```text
generated client or service registration
                |
                v
       typed operation binding
                |
                v
   shared processing and batch policy
                |
                v
 transport-specific sender or consumer
                |
                v
       Kafka / Redis / AMQP SDK
```

### Shared messaging runtime

A small broker-neutral runtime owns:

- invoking single-message or batch handlers;
- batch size and maximum-wait policy;
- application-supplied batch publication and partial-failure reporting;
- one dependency-injection scope per message or batch;
- cancellation and graceful shutdown;
- common telemetry and failure reporting;
- validation of handler registration and batch results.

It operates on typed messages and opaque delivery leases. It never interprets a
Kafka offset, Redis entry ID, or AMQP delivery tag.

### Transport runtimes

Transport packages own their native state machines:

| Transport | Runtime responsibilities |
| --- | --- |
| Kafka | polling, group membership, rebalance handling, partition ordering, offset storage and commits |
| Redis Streams | `XREAD` checkpoints; group creation, `XREADGROUP`, pending-entry recovery with `XAUTOCLAIM`/`XCLAIM`, and `XACK` |
| Redis Pub/Sub | subscription lifetime and at-most-once callback delivery |
| AMQP | queue consumption, prefetch, channel lifetime, and ack/nack settlement |

The implementation should not force these transports through a large
lowest-common-denominator `IBroker` interface. A small processing seam is useful;
hiding transport invariants is not.

## Batch Handling

Consumption batching is runtime configuration, not part of the Smithy contract.
The shared layer defines size and wait policy, while each transport forms safe
batches under its ordering and ownership constraints. Typical options are
`MaxBatchSize` and `MaxBatchWait`. They may differ by
deployment without changing the service model or handler types.

For durable transports:

1. the transport runtime receives deliveries;
2. the generated binding decodes them into typed values;
3. the shared processor invokes one handler for the complete batch;
4. only successful completion allows the transport runtime to settle it;
5. failure leaves the batch unsettled, so it can be redelivered.

Kafka advances only through the contiguous successfully processed prefix of
each topic-partition. A later success must never advance past an earlier failed
or in-flight delivery. Partition ownership must still be valid when storing the
position; ownership loss can cause replay by the new owner.
Redis acknowledges the successful entry IDs. AMQP settles the corresponding
delivery tags. There is no per-item acknowledgment in the handler contract and
no partial-success result. Applications requiring partial success should make
handlers idempotent and split work into smaller configured batches.

Ordering guarantees never exceed the underlying transport. In particular,
Kafka ordering is per partition, not across an entire multi-partition batch.

## Batch Publication

Every generated command client and event publisher also exposes an explicit
batch method:

```csharp
Task DimLightBatchAsync(
    IReadOnlyList<DimLightCommand> commands,
    CancellationToken cancellationToken = default);

Task PublishLightMeasuredBatchAsync(
    IReadOnlyList<LightMeasured> messages,
    CancellationToken cancellationToken = default);
```

An explicit batch is an application lifecycle boundary: the returned task
completes when every item reaches the transport's configured acceptance level.
It is distinct from transparent producer batching. Kafka, for example, may
coalesce several individual sends internally even when the application calls
only the single-message method.

Batch publication follows these rules:

- every input value becomes an independent broker message;
- the runtime serializes the complete batch before sending any item, so a local
  serialization failure cannot cause a partial publication;
- an empty batch completes without contacting the broker;
- input order is supplied to the transport, but ordering guarantees do not
  exceed those of the address and partition selected for each item;
- success means every item reached the configured producer-confirmation
  boundary;
- publication is not atomic unless a separate, explicit transactional feature
  says otherwise;
- a transport failure may leave some item outcomes unknown or successful.

A partial failure throws a protocol-neutral batch publication exception that
identifies successful, failed, and unknown input indexes without exposing
offsets, Redis entry IDs, or other native result types. Retrying unknown items
may create duplicates, so the contract continues to assume idempotent consumers
where duplicate delivery matters.

For request/reply commands, the client batch method returns replies in input
order. A partial timeout or reply failure reports the corresponding indexes;
success requires one reply per input. Exactly-once request/reply is not implied.

Transport runtimes may implement the operation efficiently: Kafka can enqueue
the messages for librdkafka's normal batching, Redis can pipeline `XADD`
commands, and AMQP can use publisher confirms. Those optimizations do not alter
the public semantics above.

## Hosting and Configuration

Public service types and handler interfaces do not contain protocol names.
Broker selection belongs at the composition root:

```csharp
services.AddKafkaMessaging(kafkaOptions);
services.AddStreetlightDeviceClient();
services.AddStreetlightDeviceCommandConsumer();
services.AddScoped<IDimLightHandler, DimLightHandler>();
```

Changing the transport replaces the first registration and regenerates bindings
from the selected Bote protocol; the handler remains unchanged.

As with `Map{Service}` on the HTTP server, a generated public consumer class is
not required. The generated registration can connect operation descriptors to a
generic hosted consumer. A direct-use API may accept the generated definition
and a handler, but the concrete polling worker belongs to the transport runtime.

## Infrastructure

Deployment metadata remains separate from runtime bindings. Generated desired
state such as Kafka topics and future schema-registry subjects may be consumed
by Aspire, a console reconciler, or infrastructure tooling. Creating clusters,
networks, and managed broker instances remains deployment-platform work.

## Migration

The foundational migration is implemented for Kafka and Redis: generated
operation bindings, service-role interfaces, per-operation handlers, shared
scoped processing, and transport-owned hosting and delivery state.

Remaining work:

1. add single/batch handler selection and batch publication to the shared
   processing runtime;
2. define explicit retry, dead-letter, concurrency, and telemetry policies;
3. extend the architecture to additional transports and codecs;
4. keep generated infrastructure metadata as an independent opt-in surface.

## Non-goals

- A universal abstraction that erases differences in broker delivery guarantees.
- Exactly-once side effects or exactly-once request/reply.
- Exposing offsets, entry IDs, delivery tags, or native broker messages to
  ordinary handlers.
- Treating durable broker streams as request-scoped gRPC event streams.
- Provisioning broker clusters from generated application code.
