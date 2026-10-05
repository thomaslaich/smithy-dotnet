# Streaming

How Smithy `@streaming` operations map to generated C# clients, servers,
protocols, and transports.

Smithy uses one trait, `@streaming`, for two different runtime shapes:

- **Event streams.** A streaming member targets a union. The wire is a sequence
  of logical messages, and generated C# uses `IAsyncEnumerable<TEvent>`.
- **Streaming blobs.** A streaming member targets a blob. The wire is one
  continuous body, and generated C# uses `Stream`.

The two share model detection and cancellation conventions but not framing.
Event streams are serialized per event; a streaming blob is an HTTP body with
content-length, checksum, retry, and signing concerns.

## Model Mapping

An operation is an event-stream operation when its input or output shape has a
streaming member targeting a union; the union becomes the generated event type.
It is a blob-streaming operation when the streaming member targets a blob; the
member becomes `Stream`.

Non-streaming members around the streaming member remain part of the generated
input or output structure.

## Event Streams

Event-stream operations bind through the same `IServiceProtocol` factories as
unary operations, and one operation protocol covers unary, client-streaming,
server-streaming, and duplex. Direction is a property of the modeled shapes: an
input stream is an `IAsyncEnumerable<TEvent>` member on `TInput`, an output
stream is one on `TOutput`, and a duplex operation has both. A protocol resolves
each direction into framing delegates when it binds the operation.

`EventStreamBinding.TryBind` finds a structure's event-stream member and hands
the protocol an `EventStreamBinding<TShape, TBuilder, TEvent>` with the builder
and event types in scope. The binding reads the events off a value, builds a
value around incoming events, and exposes the other members as a projection.
A structure has at most one event-stream member.

Generated operations keep the unary signature whatever the direction:

```csharp
Task<ChatOutput> ChatAsync(ChatInput input, CancellationToken cancellationToken = default);
```

The operation returns once the response headers and any initial members are
available. Events are enumerated lazily: the `IAsyncEnumerable<TEvent>` member
owns the live request or response body.

### Event Semantics

Each event is one member of the event union.

- **Framing.** REST and rpcv2Cbor frame each event as a
  `vnd.amazon.eventstream` message. `:event-type` names the union member,
  `:content-type` carries the body codec's media type, and the payload is the
  member encoded by that codec. gRPC sends each event as one length-prefixed
  protobuf message of the union.
- **Initial members.** REST binds the non-stream members through its normal
  HTTP bindings, with the event stream as the `@httpPayload`. rpcv2Cbor sends
  them as an `initial-request` or `initial-response` event ahead of the stream,
  and omits that event when there are none. gRPC rejects an event-stream shape
  with any other members.
- **Termination.** An `error` message (`:error-code`, `:error-message`) or an
  `exception` message (`:exception-type`) ends the stream, and enumeration
  throws. A gRPC server that fails mid-stream ends it with `grpc-status`
  INTERNAL, and the client throws when the final `grpc-status` is missing or
  non-zero.
- **Unknown events.** gRPC skips an event whose union case it does not
  recognize. JSON and CBOR protocols reject an unknown union member.
- **Validation.** The server validates the initial members like a unary input.
  Events are not validated.

### Framing and the Streaming Transport

The protocol owns all wire framing; no frame type crosses the transport
boundary. Client halves emit fully framed request bodies and deframe raw
response streams; server halves deframe the raw request body and emit framed
response chunks. `NSmithy.EventStream` provides `vnd.amazon.eventstream`
message encoding with CRC validation, plus the shared event semantics above in
`EventStreamEvents`. gRPC owns its 5-byte message prefix and the `grpc-status`
trailer.

Request and response streaming are independent, so the transport types are
named for what actually streams. A streaming request carries a
`SmithyHttpBody.EventStreaming` body (`IAsyncEnumerable<ReadOnlyMemory<byte>>`,
each chunk written and flushed as one unit); every other request carries
`Bytes`. The response is always one `SmithyHttpClientResponse`, and the runtime
tells the transport whether to buffer it:

```csharp
public interface IHttpTransport
{
    Task<SmithyHttpClientResponse> SendAsync(
        SmithyHttpRequest request,
        SmithyHttpClientResponseMode responseMode,
        CancellationToken cancellationToken = default);
}
```

In `Buffer` mode, `Body` is `Bytes` or `Empty`, and `Trailer` is available
immediately. In `Stream` mode, `Body` is `SmithyHttpBody.Streaming`, `Trailer`
resolves once the stream has been read to its end, and disposing the stream
releases the connection. One `HttpClient`-backed transport serves unary,
blob-streaming, and event-streaming protocols.

The server side, a shared `SmithyServerRuntime` and a protocol-neutral host
adapter, is covered in [server-architecture.md](server-architecture.md).

## Streaming Blobs

A streaming blob belongs to the unary operation path: a `GetObject`-style
operation is one request and one response, and only the payload member is a
stream. The HTTP body is a closed union rather than parallel nullable byte and
stream properties:

```csharp
public abstract record SmithyHttpBody
{
    public static SmithyHttpBody Empty { get; }

    public sealed record Bytes(byte[] Content) : SmithyHttpBody;

    public sealed record Streaming(Stream Content, long? ContentLength = null)
        : SmithyHttpBody;

    public sealed record EventStreaming(IAsyncEnumerable<ReadOnlyMemory<byte>> Content)
        : SmithyHttpBody;
}
```

`SmithyHttpRequest` and `SmithyHttpClientResponse` carry a non-nullable body
that defaults to `Empty`. A protocol binding a streaming blob uses `Streaming`.

Generated model types expose the member as `Stream`, and the other members keep
their normal header, label, and query bindings:

```csharp
public sealed record PutObjectInput(string Bucket, string Key, Stream Body);

public sealed record GetObjectOutput(Stream Body, string? ContentType);
```

### Ownership

The transport disposes a request stream once the request has been sent. The
caller owns a response stream: disposing it releases the connection.

A streaming request body cannot be replayed, so an operation with one gets
exactly one attempt regardless of the retry strategy.

## Responsibilities

Generated clients bind operation schemas once and call the selected operation
protocol; they never branch on protocol-specific streaming behavior.

Protocols own request URI, method, and header binding; payload binding; event
framing; response and error discrimination; trailer handling; and content
length and transfer encoding.

The shared runtime owns the transport-neutral request and response types,
cancellation propagation, the response buffering mode, and stream ownership.

## Package Boundaries

- `NSmithy.Core`: `EventStreamSchema<TEvent>` and `EventStreamBinding`.
- `NSmithy.EventStream`: `vnd.amazon.eventstream` message framing and the
  shared Smithy event semantics.
- `NSmithy.Http`: the body union, the transport, and the protocol interfaces.
- Protocol packages: everything wire-specific, such as gRPC's frame prefix and
  trailers, REST payload binding, and rpcv2Cbor's initial messages.

## Non-goals

- Streaming blobs never go through event-stream framing.
- Generated client and server signatures expose no gRPC-specific types.
- Native streaming requires no `Grpc.Net`, `Grpc.Tools`, or protocol-specific
  generated code.
- SigV4 signs buffered payloads only. Chunked streaming signatures are out of
  scope, so a streaming request body cannot be header-signed.
