# Native gRPC (own proto codec + gRPC protocol)

How NSmithy speaks gRPC natively — its own proto codec and gRPC protocol, with no
`protoc`, `Grpc.Tools`, or `Grpc.Net`.

## Goal

Speak gRPC end to end without `protoc`, `Grpc.Tools`, or `Grpc.Net` — the same
way NSmithy already speaks rpcv2Cbor and REST. gRPC becomes "just another
protocol": a binary codec plus a transport binding that plugs into the existing
`IServiceProtocol` / `IOperationProtocol<TInput, TOutput>` abstraction.

## The big picture

One Smithy model feeds two code-generation paths. NSmithy generates the native
client/server directly (no protoc); the legacy `.proto` emission is still
available so a `Grpc.Net` peer can be generated the conventional way. Both peers
speak the same gRPC wire contract, so they interoperate freely.

```
┌───────────────────────┐                          ┌───────────────────────┐
│   1. Smithy model     │ ───────────────────────► │   2. .proto file      │
└───────────┬───────────┘   smithy-proto-codegen   └───────────┬───────────┘
            │                                                  │
            │ smithy-csharp-codegen        protoc / Grpc.Tools │
            │                                                  │
            ▼                                                  ▼
┌───────────────────────┐                          ┌───────────────────────┐
│  3. NSmithy native    │ ◄──────────────────────► │  4. Grpc.Net          │
│     client / server   │         gRPC wire        │     client / server   │
│  (ProtoCodec +        │    HTTP/2 + protobuf     │                       │
│   GrpcProtocol)       │      (interoperable)     │                       │
└───────────────────────┘                          └───────────────────────┘
```

- **1 → 2** `smithy-proto-codegen` emits the `.proto` (legacy / for external peers).
- **1 → 3** `smithy-csharp-codegen` emits the native NSmithy client/server.
- **2 → 4** the `.proto` feeds protoc/`Grpc.Tools` to build a `Grpc.Net` peer.
- **3 ↔ 4** native and `Grpc.Net` peers talk to each other over the same gRPC
  wire — either side can be client or server. This interop is the correctness
  bar for the native stack.

## Why this is a good fit

The runtime is already factored exactly the way this needs:

- **Codecs are schema-driven.** `CborCodec.FromSchema(schema)` walks a
  `Schema<T>` and emits/reads bytes. A protobuf codec is the same walk over the
  same schemas, reading the `alloy.proto#protoIndex` / `alloy.proto#protoNumType`
  traits that the model already carries (see `examples/grpc/.../library.smithy`).
- **Protocols are pluggable.** `RpcV2CborProtocol.ForService(serviceSchema)`
  returns an `IServiceProtocol`; the generated client/server glue calls only the
  protocol interface. The interface doc comment in
  `packages/NSmithy.Http/IOperationProtocol.cs` *already* anticipates gRPC:
  > "the `grpc-status` trailer for gRPC".

So a native gRPC stack is two new packages slotted beside the CBOR ones, with no
runtime-abstraction changes.

## What the old path did (being replaced)

1. `smithy-csharp-codegen` emits a `.proto` from the model.
2. `Grpc.Tools` (protoc) compiles it to C# message types + `ServiceBase`/`Client`.
3. Generated `…GrpcAdapter` translates between the protobuf types and the Smithy
   domain records.

That means two parallel object models (protobuf-generated vs. Smithy) and a
protoc/MSBuild dependency. The native path deletes (1)-(3) in favor of encoding
the Smithy records directly.

## What landed in this exploration

### `packages/NSmithy.Codecs.Proto` — protobuf wire codec

- `ProtoWire.cs` — low-level `ProtoWriter`/`ProtoReader` (varint, zigzag,
  fixed32/64, length-delimited, tags, field-skip).
- `ProtoCodec.cs` — `ProtoCodec.FromSchema<T>(schema)` → `ICodec<T>`, mirroring
  `CborCodec`. Field numbers come from `@protoIndex`; integer wire types from
  `@protoNumType` (`SIGNED`→sint, `UNSIGNED`→uint, `FIXED`→fixed, `FIXED_SIGNED`
  →sfixed). proto3 presence rules: absent (null) members are omitted; nullable
  members with a set value are emitted even when zero (explicit presence).

Covered: structures, full scalar set + numtype variants, `repeated` (packed for
scalars, length-delimited otherwise), dense maps, `intEnum`, `Timestamp` (as
`google.protobuf.Timestamp`), and unions (wrapper message whose single set field
selects the case).

Tests (`tests/.../ProtoCodecTests.cs`) assert **byte-level** equality against
hand-computed proto3 encodings (e.g. `"hi", 300` → `0A 02 68 69 10 AC 02`), which
is the real proof of interoperability, plus zigzag/fixed/packed encodings and a
rich nested/map/enum/timestamp round trip.

### `packages/NSmithy.Protocols.Grpc` — gRPC transport binding

- `GrpcMessageFraming.cs` — the 5-byte length-prefixed frame (1 compression byte
  + 4 big-endian length).
- `GrpcStatus.cs` — canonical gRPC status codes + HTTP→gRPC status mapping.
- `GrpcProtocol.cs` — `GrpcProtocol.ForService(serviceSchema)` implementing
  `IServiceProtocol`/`IOperationProtocol`. Method path `/{namespace}.{Service}/{Method}`,
  content-type `application/grpc+proto`, `te: trailers`, framed proto bodies, and
  the `grpc-status`/`grpc-message` trailer error model (carried on
  `SmithyHttpResponse.Headers`, which the transport renders as HTTP/2 trailers).

Tests (`tests/.../GrpcProtocolTests.cs`) round-trip client→server→client through
the protocol interface and cover framing + the modeled-error path. No
protoc/Grpc.* anywhere.

## Unary coverage: complete

The full unary `alloy.proto#grpc` surface now works, verified end-to-end by the
`examples/grpc` library service (no protoc/Grpc.Tools/Grpc.Net):

- **String enums** → proto enum ordinals (`UNSPECIFIED=0`, then declaration order),
  read from the `smithy.synthetic#enum` trait the schema already carries.
- **`@protoInlinedOneOf`** → the union's case fields are written/read directly in
  the parent message's field-number space.
- **`@sparse` maps** and **`Document`** → `google.protobuf.Value` (null/bool/
  number/string/struct/list), so null map entries round-trip.
- Client surfaces a non-gRPC/non-200 response as a clear transport error instead
  of failing while parsing the body as a frame.

## Remaining gaps

- **Streaming** — unary only; `@streaming` needs a separate streaming protocol
  interface (the proto `stream` keyword is already modeled in codegen). This is
  the one large remaining piece.
- **`@sparse` map values that are aggregates** (struct/list) — only scalar sparse
  values map to `Value` today; the example uses `map<string,string>`.
- **`@protoNumType` on list/map elements** — honored on struct members only.

## What landed in codegen + runtime

- **Runtime trailer support.** `HttpClientTransport` folds HTTP/2 trailing
  headers into the response header dictionary (so the client sees `grpc-status`),
  and `SmithyAspNetCoreProtocol.WriteSmithyGrpcResponseAsync` emits
  `grpc-status`/`grpc-message` as real HTTP/2 trailers (falling back to headers
  when trailers are unsupported).
- **Native server codegen.** `ServerGenerator` emits `Map{Service}Grpc` using
  `new GrpcProtocol().ForService(...)` over the ASP.NET helpers — the
  protoc-generated `…GrpcAdapter` / `MapGrpcService` path is gone. It coexists with
  the REST map for dual-protocol services.
- **Native client codegen.** `ClientGenerator` emits a single `{Service}Client`
  whose protocol is chosen by an optional constructor parameter
  (`new {Service}Client(endpoint, protocol: new GrpcProtocol())`, defaulting to the
  primary declared protocol) through the *same* invoker/protocol machinery as the
  rpc client (gRPC is a `ProtocolSupport.Kind`), bound to `GrpcProtocol` over an
  HTTP/2 `HttpClient` — no `GrpcChannel`/`Grpc.Net.Client`. The client configures
  HTTP/2 automatically when `IProtocol.RequiresHttp2`. For a service that declares
  both an HTTP protocol and `@grpc`, the one client speaks either.
- **Runtime member traits already flow.** `SchemaGenerator.memberTraitsExpr`
  emits *all* member traits (including `@protoIndex` / `@protoNumType`) into the
  generated `Schema<T>`, so `ProtoCodec` reads them at runtime with no further
  codegen change.

## Status

`examples/grpc` is fully native and **runs end-to-end** —
`NSmithy.Protocols.Grpc` replaces `Grpc.AspNetCore`/`Grpc.Tools`/`Grpc.Net.Client`,
`AddGrpc()` is gone, the client runs over an HTTP/2 `HttpClient`, and the server
maps via `GrpcProtocol.ForService(...)`. `GetBook`, `ListBooks` (with the
`@protoInlinedOneOf` filter), `CreateBook`, `SearchBooks`, `UploadBooks`, and
`DeleteBook` all work, exercising string enums, sparse maps with nulls, intEnum,
fixed64, and Timestamp.

One HTTP detail mattered: `HttpClientTransport` now sets the request
`Version`/`VersionPolicy` from the `HttpClient` defaults — otherwise a new
`HttpRequestMessage` defaults to HTTP/1.1 and silently downgrades the gRPC call.

## Next steps

1. **Streaming** — the large remaining `alloy.grpc` piece: a streaming protocol
   interface + duplex frame streaming over HTTP/2, plus codegen for the streaming
   operation signatures.
2. **End-to-end interop test** — native server ↔ native client (works today),
   plus native ↔ a real `Grpc.Net` peer built from the emitted `.proto`
   (box **3 ↔ 4**), as an automated test.
3. **Smaller**: aggregate `@sparse` map values, `@protoNumType` on list/map
   elements, and gRPC niceties beyond `alloy.grpc` (compression, deadlines,
   metadata).
