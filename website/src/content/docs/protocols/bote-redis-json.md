---
title: Redis JSON
description: Typed Redis Streams and Pub/Sub clients, publishers, and handlers.
---

`NSmithy.Bote` generates typed operation bindings for `bote#redisStreamsJson` and
`bote#redisPubSubJson`. `NSmithy.Messaging.Redis` executes those bindings using
[StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/).
Add the build extension, JSON codec, and runtime to the generating application:

```xml
<PackageReference Include="NSmithy.Bote" Version="NSMITHY_VERSION" />
<PackageReference Include="NSmithy.Codecs.Json" Version="NSMITHY_VERSION" />
<PackageReference Include="NSmithy.Messaging.Redis" Version="NSMITHY_VERSION" />
```

The Bote package carries the generator's Maven dependencies. Consumers do not
install Maven or a JRE. Set `SmithyGenerateDependencyInjection` to `true` to generate
service registration helpers.

## Service roles

For a service such as `ChatRoom`, the generated API separates its roles:

- `IChatRoomClient` sends `PostMessageAsync` commands to the owner.
- `IChatRoomEventPublisher` publishes `PublishMessagePostedAsync` events.
- `IPostMessageHandler.HandleAsync` handles a typed command.
- `IReadMessagesHandler.HandleAsync` handles the modeled `ChatEvents` union.

Handlers receive model values and a cancellation token. Redis entries, channels,
consumer groups, and cursors stay inside the runtime. Broker subscriptions do not
return `IAsyncEnumerable<T>`.

## Redis Streams

Use `@redisStreamAdd` for commands and `@redisStreamRead` for event streams:

```smithy
@redisStreamsJson
service ChatRoom {
    version: "1.0"
    operations: [PostMessage, ReadMessages]
}

@redisStreamAdd(stream: "chat:commands", maxLen: 10000)
operation PostMessage { input: PostMessageCommand }

@redisStreamRead(stream: "chat:events", maxLen: 10000)
operation ReadMessages {
    output := { messages: ChatEvents }
}

@command structure PostMessageCommand { body: String }
@event structure MessagePosted { body: String }
@streaming union ChatEvents { messagePosted: MessagePosted }
```

Configure the connection and consumer group in the application:

```csharp
await using var connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRedisStreamsMessaging(connection);
builder.Services.AddChatRoomEventPublisher();
builder.Services.AddChatRoomCommandConsumer(new RedisStreamConsumerOptions
{
    ConsumerGroup = "chat-owner",
    ConsumerName = $"replica-{Environment.ProcessId}",
});
builder.Services.AddScoped<IPostMessageHandler, ChatOwner>();
await builder.Build().RunAsync();
```

The caller owns the connection and disposes it after the host stops. The runtime
creates a fresh DI scope per message and waits for asynchronous scope disposal
before settlement. Missing handler registrations fail startup.

Consumer groups are the default. Replicas in the same group share work through
`XREADGROUP`. The runtime scans pending entries with `XAUTOCLAIM`, including scan
pages that contain no claimed messages, and acknowledges successful entries with
`XACK`. This requires Redis 6.2 or later. Decode or handler failure stops the
worker without acknowledgment; a replacement consumer can recover the entry
after its idle threshold. By default, worker failure stops the .NET host.

`RedisStreamConsumerOptions` configures `ReadCount`, `PollInterval`, `MinIdleTime`,
and `ClaimInterval`. Set `MinIdleTime` above normal processing time for the fetched
entries to avoid reclaiming work from a healthy consumer. Redelivery and
concurrent recovery can produce duplicates; consumers must be idempotent where
that matters. Group processing does not promise global ordering across replicas.
Retention configured with `maxLen` can remove messages before processing or recovery.

### Independent readers and checkpoints

When every reader must observe every event, select independent `XREAD` processing:

```csharp
builder.Services.AddChatRoomEventConsumer(new RedisStreamConsumerOptions
{
    ReadMode = RedisStreamReadMode.Independent,
    StartPosition = "$",
    CheckpointName = "dashboard-1",
    CheckpointStore = new RedisStreamCheckpointStore(connection),
});
builder.Services.AddScoped<IReadMessagesHandler, ChatReader>();
```

The runtime loads a saved cursor at startup and persists each new position only
after handler success. A saved cursor takes precedence over `StartPosition`.
`"0-0"` reads from the beginning; `"$"` starts at the current end, resolved before
startup completes. Without a checkpoint store, progress survives only in memory.

Use a stable, distinct `CheckpointName` per logical reader, with one active
reader per checkpoint. The supplied store persists positions in Redis; its
durability follows that Redis deployment. Implement `IRedisStreamCheckpointStore`
to use another persistence mechanism. Cursor values never reach handlers.

### Request/reply

An output annotated `@reply` makes a stream command a request/reply operation:

```smithy
@redisStreamAdd(stream: "inventory:queries", maxLen: 10000)
operation GetStock {
    input: GetStockRequest
    output: GetStockReply
}

@command structure GetStockRequest { productId: String }
@reply structure GetStockReply { productId: String, available: Integer }
```

`IInventoryClient.GetStockAsync` returns `Task<GetStockOutput>`.
`IGetStockHandler.HandleAsync` accepts `GetStockInput` and returns the same typed
output. Timeout configuration belongs to `RedisMessagingOptions.RequestTimeout`
(default: 30 seconds), rather than the generated method signature.

The runtime serializes the request, subscribes to a temporary reply channel, and
then appends `data`, `reply_to`, and `correlation_id` to the request stream. The
owner processes the request, publishes the correlated JSON reply, and only then
acknowledges the request. The requester removes its subscription on success,
failure, or cancellation. Subscription establishment is allowed to finish before
cleanup, so cancellation cannot leave a late subscription behind; Redis command
timeouts bound that establishment wait.

A request timeout does not retract an appended request. Requests may be replayed,
and replies use at-most-once Pub/Sub delivery. A disconnected requester can miss
a reply, and recovery can publish duplicate replies. Exactly-once request/reply
is not promised. Request/reply consumers require consumer-group mode.

## Redis Pub/Sub

Use `@redisPublish` for one-way commands and `@redisSubscribe` for event unions.
The generated client, publisher, and operation-handler pattern is the same as
for Streams. Select `AddRedisPubSubMessaging(connection)` at the composition root
and register the generated command or event consumer.

`RedisPubSubConsumerOptions.Capacity` bounds the runtime's dispatch queue
(default: 1024). Subscription callbacks enqueue messages; scoped handlers process
them sequentially. A handler failure or a full queue stops the worker and removes
its subscriptions. Startup waits for subscriptions to be established before
returning. Pub/Sub cannot acknowledge or replay messages, so failures and overload
can lose messages even though the handler interface matches durable transports.
Broker/client SDK buffers are separate from the bounded runtime queue.

Command and event traffic must use distinct stream keys or Pub/Sub channels.
Their JSON encodings distinguish event variants but have no command-versus-event
direction discriminator. Batching and configurable automatic retry/dead-letter
policies are not implemented yet.

## Examples and AsyncAPI

The [Redis examples](https://github.com/thomaslaich/smithy-dotnet/tree/main/examples/redisjson)
include chat with shared command processing and independent event readers, plus
an inventory request/reply client and server.

Set `SmithyGenerateAsyncApi` to `true` to generate AsyncAPI 3.1 and copy it to
`wwwroot/asyncapi.json`. Redis Streams replies describe the dynamic `reply_to`
channel and correlation metadata.
