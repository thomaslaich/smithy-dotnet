# Streaming Architecture

NSmithy keeps the same operation-shaped API for unary and streaming calls:
`Task<TOutput> OperationAsync(TInput input, CancellationToken ct)`. A streaming
member changes how part of the input or output is consumed, and how long the
underlying request stays alive.

This document explains the implemented HTTP/gRPC design, the reasons for its
public API, and the lifetime rules it needs to uphold. Outstanding ownership
issues are listed at the end. Durable broker consumption has a different
completion boundary and is covered in [messaging-architecture.md](messaging-architecture.md).

## What is streamed

Smithy's `@streaming` trait has two mappings in generated C#:

| Modeled target | Generated member | Meaning |
| --- | --- | --- |
| Union | `IAsyncEnumerable<TEvent>` | A sequence of separately encoded, typed events |
| Blob | `Stream` | A continuous sequence of bytes |

The stream remains a member of the modeled input or output structure. Other
members, such as a room name, content type, or resource identifier, retain their
normal protocol bindings. Whether a protocol can carry those members alongside
an event stream depends on its wire format. The current native gRPC binding,
for example, rejects non-streaming initial members on event-stream shapes.

Both forms use the ordinary operation protocol interfaces. They need different
serialization strategies: an event stream frames each event, while a blob
stream transfers bytes without interpreting them as modeled messages.

## Why the operation still returns a task

An operation has two useful milestones: obtaining its output object and
finishing its output body. A buffered response usually makes them look like one
step. A streaming response separates them.

Consider these simplified shapes:

```csharp
public sealed record ChatInput(IAsyncEnumerable<ChatEvent> Events);
public sealed record ChatOutput(IAsyncEnumerable<ChatEvent> Events);

Task<ChatOutput> ChatAsync(ChatInput input, CancellationToken ct);
```

On the **server**, completing `ChatAsync` gives the runtime an output object to
serialize. Its event sequence can still be waiting for future messages. The
handler must return that object before the runtime can begin writing its events.

On the **client**, awaiting `ChatAsync` obtains the output after the response
headers and any protocol-required initial data have been read. It does not
buffer the event sequence or wait for the conversation to finish:

```csharp
var output = await client.ChatAsync(new ChatInput(outgoingEvents), ct);
await foreach (var message in output.Events.WithCancellation(ct))
{
    // Process an incoming event while outgoingEvents can still produce more.
}
```

The input and output directions are independent:

| Operation form | Input | Output | Typical server behavior |
| --- | --- | --- | --- |
| Unary | Value | Value | Compute and return a result |
| Server streaming | Value | Event sequence | Return an output containing a lazy sequence |
| Client streaming | Event sequence | Value | Read the input sequence, then return a summary |
| Bidirectional streaming | Event sequence | Event sequence | Return a lazy output sequence and process both directions concurrently |

Keeping the modeled structures preserves one generated method pattern and leaves
room for metadata around a stream. A gRPC-specific API can instead take an
`IAsyncStreamReader<T>` and `IServerStreamWriter<T>` directly, as the
[Grpc.Net chat server](../examples/grpc/streaming/grpcnet-server/Program.cs) does.
NSmithy's API keeps those transport types out of business handlers. With the
writer-based API, the server method stays active while it writes; with NSmithy,
the returned output iterator carries that continuing work.

## Start with a simple server

The server counterpart to the client above can be an echo handler. Using the
REST JSON chat model, which includes a room name, its complete streaming behavior
is:

```csharp
public Task<ChatOutput> ChatAsync(
    ChatInput input,
    CancellationToken ct = default)
{
    var events = input.Events ?? AsyncEnumerable.Empty<ChatEvent>();
    return Task.FromResult(new ChatOutput(input.Room, EchoAsync(events, ct)));
}

private static async IAsyncEnumerable<ChatEvent> EchoAsync(
    IAsyncEnumerable<ChatEvent> incoming,
    [System.Runtime.CompilerServices.EnumeratorCancellation]
    CancellationToken ct = default)
{
    await foreach (var item in incoming.WithCancellation(ct))
    {
        yield return item;
    }
}
```

There are two parts because they finish at different times. `ChatAsync` supplies
the room metadata and the output sequence immediately. `EchoAsync` supplies each
response event when it becomes available. `Task.FromResult` completes the first
part; it does not run the iterator or collect its events.

The runtime enumerates `EchoAsync` while writing the response. Each iteration
awaits an incoming event, then yields an outgoing event. This simple interaction
needs no background task or channel. When the input ends, the iterator ends, so
the output ends too. It deliberately ties each response to an input message.

The client supplies a sequence for the transport to read and then enumerates the
response. The server receives a sequence and returns another for the runtime to
enumerate. That is the API symmetry; the application decides how the two
sequences relate.

## Why the broadcast chat example is more involved

The [native chat server](../examples/grpc/streaming/server/Program.cs) returns a
`ChatOutput` containing `ChatEventsAsync(...)`. Calling an async iterator method
creates a sequence; its body starts when the runtime enumerates it.

The example then runs two activities for the same call:

```text
request body -> decode events -> inbound task -> publish to room subscribers

this subscriber's room channel -> output iterator -> encode events -> response body
```

When output enumeration begins, the iterator joins the room and starts a task
that reads `input.Events`. That task publishes incoming messages to the other
participants. Meanwhile, the iterator reads its own room channel and yields
messages for the response. A participant can therefore receive messages while
its own input is idle.

Reading all input before returning the output would prevent this interaction:
for a long-lived chat, the input may never finish. The concurrent reader is an
application decision; neither the generated handler interface nor the protocol
creates a chat room or broadcasts messages.

The sample uses unbounded, in-memory channels. They demonstrate fan-out but do
not provide durable storage or a bound on queued messages. A deployed chat
service needs an explicit slow-subscriber policy.

## Completion, cancellation, and failure

Returning an output object does not finish a streaming call. The response body
continues until its iterator completes, fails, or is cancelled. Errors can thus
occur either while awaiting the operation or later while enumerating events.

An input sequence ending is an **input half-close**: the peer has finished
sending. It does not inherently require the output sequence to end. In the chat
example, the participant can keep receiving other people's messages after its
input ends. An upload operation instead reads to the end and returns a final
summary. The handler chooses the relationship between the two directions.

Cancellation must reach both activities of a duplex handler. Iterators should
propagate the call token to reads, writes, and waits, and release subscriptions
in `finally`. The chat example removes its subscription and waits for the inbound
task there. Merely disposing an output iterator is not a general mechanism for
cancelling an independent input task; an application that permits early output
termination must also arrange to stop that task.

Wire-level completion belongs to the protocol. For gRPC, reaching the end of the
events is followed by checking `grpc-status`; receiving some events does not
establish that the call succeeded. If a server iterator fails after headers have
been sent, the host cannot replace the response with a new ordinary error
response. The ASP.NET Core adapter passes the failure to the protocol's trailer
factory when trailers are supported; otherwise the failure aborts the response.

Pulling events through `IAsyncEnumerable<T>` lets each stage await the next
stage, but it does not guarantee bounded memory throughout an application.
Queues, transport buffers, and independently running producers have their own
capacity and lifetime rules.

## Where the behavior lives

Generated code describes the service, operation, and typed shapes. Protocols
bind those schemas once and select serialization strategies for each direction.
The client and server runtimes execute the same operation lifecycle used for
unary calls.

| Layer | Responsibility |
| --- | --- |
| Generated client and handler | Modeled method signatures and operation bindings |
| Protocol | Initial data, payload codecs, event framing, and protocol errors/trailers |
| Client/server runtime | Invoke the bound operation through the shared lifecycle |
| HTTP transport or host adapter | Move bytes, propagate cancellation, and manage HTTP resources |
| Application | Produce and consume events; coordinate duplex work and any queues |

The common interfaces are
[`IClientOperationProtocol<TInput, TOutput>` and `IServerOperationProtocol<TInput, TOutput>`](../packages/NSmithy.Http/OperationProtocols.cs).
There is no additional operation interface for each streaming direction.

The byte boundary is deliberately separate from the typed event boundary:

- On the client request side, `SmithyHttpBody.Bytes` carries a buffered payload,
  `Streaming` carries a byte stream, and `EventStreaming` carries asynchronously
  produced chunks already framed by the protocol.
- On the client response side, `IHttpTransport` can buffer the body or return a
  live stream. The protocol decodes that stream into the output's typed events
  or exposes it as a blob member.
- On the server side, the host exposes a live request body when needed. The
  protocol returns `SmithyHttpServerResponse.Body` as write-ready chunks; the
  host writes and flushes them and emits the protocol's trailers.

A chunk is a unit of application writing, not a guarantee about network packet
or read boundaries. The receiver's protocol parser reconstructs complete frames
from the byte stream. gRPC and AWS event-stream protocols own their respective
framing rules; the transport does not decode modeled events.

See [http-interfaces.md](http-interfaces.md),
[client-architecture.md](client-architecture.md), and
[server-architecture.md](server-architecture.md) for the surrounding lifecycle.

## Blob streams and resource ownership

A blob operation uses the same method pattern. For example, an upload input can
contain `Stream Body`, while a download output can contain `Stream Body` and a
content type. There is no event union or per-event framing. The caller reads or
writes bytes using normal stream operations.

Returning a live response transfers responsibility for finishing or abandoning
that body. `HttpClientTransport` wraps live responses in a stream whose disposal
also disposes the underlying HTTP response. A caller receiving a blob stream
must dispose that stream. Generated output records do not currently provide a
common disposable wrapper for all streaming outputs.

For event responses, cleanup belongs to the protocol's iterator. The gRPC
response iterator disposes the body when an active enumeration exits, including
an early `break` from `await foreach`. That is different from obtaining an output
and never starting enumeration: iterator cleanup has not run in that case.
Treat a live event sequence as a single consumption of the response, rather than
as a replayable collection.

Request-body ownership also needs care. The current `HttpClientTransport` wraps
an outgoing blob in `StreamContent` and disposes its request message, which can
dispose the supplied stream. A caller-owned, leave-open contract is therefore an
intended improvement, not a guarantee of the current implementation.

## Why broker messaging uses handlers

HTTP and gRPC streams belong to a request and connection. They do not promise
that an event will be replayed until the receiving application processes it.
`IAsyncEnumerable<T>` expresses their incremental consumption naturally.

A durable broker needs a separate processing decision: successful handler
completion permits acknowledgment; failure leaves a delivery available for
recovery according to the transport's rules. Yielding a bare model value cannot
report whether the caller processed it successfully. Broker consumption therefore
uses the handlers described in [messaging-architecture.md](messaging-architecture.md).
Using the same Smithy `@streaming` union does not imply the same delivery lifetime.

## Remaining design work

The shared operation interfaces and protocol-owned framing are implemented.
The following lifetime and policy questions remain:

- Provide an explicit way to dispose a streaming output even if its event
  sequence was never enumerated, and make blob ownership equally clear.
- Preserve caller ownership of outgoing blob streams rather than letting HTTP
  request disposal close them implicitly.
- Define replay support before retrying streamed requests. The current client
  runtime considers only empty and buffered byte bodies replayable; it does not
  automatically retry a streaming body.
- Keep operation-start latency and stream duration distinct in telemetry, and
  specify how cancellation, partial consumption, and late protocol errors affect
  completion reporting.

Streaming authentication and checksums must follow each protocol's requirements.
Any buffering, replay, or incremental signing needed for them belongs in the
protocol and client lifecycle, without changing the generated handler signature.
