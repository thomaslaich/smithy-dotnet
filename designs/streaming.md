# Streaming Design

Target architecture for Smithy `@streaming` operations in generated C# clients,
servers, protocols, and transports.

## Goal

Smithy uses one trait, `@streaming`, for two different runtime shapes:

- **Event streams** — a streaming member targets a union. The wire is a sequence
  of logical messages. Generated .NET surfaces use `IAsyncEnumerable<TEvent>`.
- **Streaming payload blobs** — a streaming member targets a blob. The wire is
  one continuous body. Generated .NET surfaces use `Stream`.

These shapes should share model detection and cancellation conventions, but they
should not share one operation protocol interface. Event streams are framed and
serialized per event. Blob streams are HTTP body streams with content length,
range, checksum, retry, and signing concerns.

## Model Mapping

An operation is an event-stream operation when its input or output shape has a
streaming member whose target is a union. The streaming member's target becomes
the generated event type.

An operation is a blob-streaming operation when its input or output shape has a
streaming member whose target is a blob. The streaming member becomes `Stream`
in generated C#.

Operations can still have non-streaming members around the streaming member.
Those members remain part of the generated input/output structure and bind using
the protocol's normal rules.

## Event Streams

Event-stream operations are bound through the same `IServiceProtocol` that hands
out unary operations. Alongside `ForOperation`, it exposes three event-stream
binding methods with default implementations that throw `NotSupportedException`,
so a protocol opts in only to the streaming shapes it actually supports:

```csharp
public interface IServiceProtocol
{
    IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation);

    IOutputEventStreamOperationProtocol<TInput, TOutputEvent>
        ForOutputEventStreamOperation<TInput, TOutput, TOutputEvent>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent)
        => throw new NotSupportedException();

    IInputEventStreamOperationProtocol<TInputEvent, TOutput>
        ForInputEventStreamOperation<TInput, TInputEvent, TOutput>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent)
        => throw new NotSupportedException();

    IDuplexEventStreamOperationProtocol<TInputEvent, TOutputEvent>
        ForDuplexEventStreamOperation<TInput, TOutput, TInputEvent, TOutputEvent>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            Schema<TOutputEvent> outputEvent)
        => throw new NotSupportedException();
}
```

Operation interfaces are named by stream direction (output / input / duplex —
where the `@streaming` member sits in the model) and split by call side, the
same client/server split the unary `IOperationProtocol` uses: each direction
has a `…ClientProtocol` and a `…ServerProtocol` half, and the combined
interface protocol implementations implement. The three directions stay
separate interfaces — their signatures differ in meaningful ways, and merging
them would force nullable or unused members into at least two cases.

### Generated API

Server streaming:

```csharp
IAsyncEnumerable<ChatEvent> WatchRoomAsync(
    WatchRoomInput input,
    CancellationToken cancellationToken = default);
```

Client streaming:

```csharp
Task<UploadTranscriptOutput> UploadTranscriptAsync(
    IAsyncEnumerable<ChatEvent> input,
    CancellationToken cancellationToken = default);
```

Bidirectional streaming:

```csharp
IAsyncEnumerable<ChatEvent> ChatAsync(
    IAsyncEnumerable<ChatEvent> input,
    CancellationToken cancellationToken = default);
```

Output streams are cold. The transport call starts when the caller enumerates
the returned `IAsyncEnumerable<TEvent>`, and enumeration cancellation is the
primary cancellation path for the stream.

### Framing and the Streaming Transport

The protocol owns all wire framing; there is no shared frame type at the
transport boundary. Client halves emit fully framed request bodies and deframe
raw response streams themselves; server halves deframe the raw request body and
emit framed response chunks:

- gRPC owns the 5-byte message prefix and validates the `grpc-status` HTTP/2
  trailer after the response stream ends.
- AWS event stream protocols own `vnd.amazon.eventstream` message framing,
  typed per-message headers, and CRC validation.
- Other event-stream protocols can provide their own frame encoding without
  changing generated client signatures.

The request and response streaming axes are independent, so the transport types
are named for what actually streams. A streaming request body is a variant of
the `SmithyHttpBody` union, `EventStreaming` (`IAsyncEnumerable<ReadOnlyMemory<byte>>`,
each chunk written and flushed as one unit), so every client request is a
`SmithyHttpRequest`: output-stream requests carry a `Bytes` body (unary),
	input-stream and duplex requests carry an `EventStreaming` body. The response is
	a single `SmithyHttpResponse`; the runtime asks the transport for either a
	buffered body or a live body:

```csharp
public interface IHttpTransport
{
    Task<SmithyHttpResponse> SendAsync(
        SmithyHttpRequest request,
        SmithyHttpResponseMode responseMode,
        CancellationToken cancellationToken = default);
}
```

In `Buffer` mode, `SmithyHttpResponse.Body` is `Bytes` or `Empty`, and trailers
are available through `Trailer` because the body has already been read. In
`Stream` mode, `Body` is `SmithyHttpBody.Streaming`, `Trailer` resolves HTTP
trailing headers once the stream is read to its end, and disposing the stream
releases the connection. The same `HttpClient`-backed transport serves unary,
blob-streaming, and event-streaming protocols.
The server side of this architecture — a shared `SmithyServerRuntime` and a
protocol-neutral host adapter — is covered in
[server-architecture.md](server-architecture.md).

## Streaming Blob Payloads

Streaming blob payloads belong to the unary operation path. A `GetObject`-style
operation is still one request and one response; only the payload member is a
stream.

The HTTP model represents the body as an explicit abstraction rather than
parallel nullable byte and stream properties:

```csharp
public abstract record SmithyHttpBody
{
    public static SmithyHttpBody Empty { get; }

    public sealed record Bytes(byte[] Content) : SmithyHttpBody;

    public sealed record Streaming(
        System.IO.Stream Content,
        long? ContentLength = null
    ) : SmithyHttpBody;
}
```

`SmithyHttpRequest` and `SmithyHttpResponse` carry a non-nullable
`SmithyHttpBody` that defaults to `SmithyHttpBody.Empty`. Protocols that buffer
payloads use `SmithyHttpBody.Bytes`; protocols that bind a streaming blob payload
use `SmithyHttpBody.Streaming`.

### Generated API

Generated model types expose streaming blob members as `Stream`:

```csharp
public sealed record PutObjectInput(
    string Bucket,
    string Key,
    Stream Body);

public sealed record GetObjectOutput(
    Stream Body,
    string? ContentType);
```

The containing input/output structure remains the operation's typed surface, so
headers, labels, query members, and metadata keep their normal bindings.

### Ownership

Request streams are owned by the caller. Generated clients read them but do not
dispose them.

Response streams are owned by the response object returned to the caller. The
generated output type should make disposal clear, either by implementing
`IDisposable`/`IAsyncDisposable` when it owns a stream member or by wrapping the
stream in an explicit response body type that owns disposal.

## Protocol Responsibilities

Generated clients should not branch on protocol-specific streaming behavior.
They should bind operation schemas once, then call the selected protocol's unary
or event-stream operation protocol.

Protocols own:

- request URI/method/header binding
- payload binding
- event-frame wire encoding
- response/error discrimination
- trailer handling
- content length and transfer encoding
- checksum and signing hooks required by their wire format

The shared runtime owns:

- transport-neutral request/response shapes
- cancellation propagation
- async enumeration helpers
- stream/body ownership conventions

## Auth, Checksums, Retries, And Telemetry

Streaming bodies affect cross-cutting client behavior:

- Payload signing may require buffered hashes, `UNSIGNED-PAYLOAD`, or chunked
  streaming signatures.
- Checksums may need incremental calculation instead of buffering.
- Retries are safe only when a request stream can be replayed or the caller opts
  into a replay strategy.
- Telemetry should measure both operation latency and stream duration.

These concerns should attach to the client lifecycle through interceptors and
typed execution context, not ad hoc protocol hooks.

## Package Boundaries

`NSmithy.Http` contains protocol-neutral concepts:

- unary request/response body abstraction
- event-stream request/response frame abstraction
- event-stream protocol interfaces
- transport abstractions

Protocol packages own wire details:

- gRPC frame prefix, HTTP/2 status/trailers, and protobuf event payloads
- REST payload binding and streamed HTTP bodies
- future AWS event-stream message framing

## Non-goals

- Do not route streaming blobs through event-stream interfaces.
- Do not expose gRPC-specific types in generated client or server signatures.
- Do not require `Grpc.Net`, `Grpc.Tools`, or protocol-specific generated code
  for native NSmithy streaming.
- Do not commit to AWS SDK-level streaming SigV4 behavior until the auth stack
  has a proper identity/signer split.
