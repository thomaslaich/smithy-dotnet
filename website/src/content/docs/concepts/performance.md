---
title: Performance
description: How NSmithy's codecs, clients, and servers compare with hand-written and generated alternatives, where it is faster and where it is slower.
---

The [benchmark suite](https://github.com/thomaslaich/smithy-dotnet/tree/main/benchmarks)
compares NSmithy with hand-written ASP.NET Core code and with other generators
on one Smithy contract. Before anything is timed, a parity check confirms that
every stack sends and receives the same bytes for every scenario, and the
hand-written baselines validate input by hand so they do the same work.

## What is measured

| Suite | Measures | Compared with |
| --- | --- | --- |
| Codec | Typed object to bytes and back | `System.Text.Json` source generation, Google.Protobuf |
| Client | Request building and response parsing, against replayed responses | A hand-written `HttpClient` client, NSwag, Grpc.Net |
| Server | HTTP bytes in, bytes out, on an in-memory test server | A hand-written minimal API, a hand-written MVC controller, the TypeSpec C# server emitter, Grpc.AspNetCore |

Everything runs in process, with no sockets. Throughput and tail latency under
concurrent load are not measured.

The numbers below come from the committed reports of 2026-11-14, measured
before the 0.20.0 release on an Apple M4 Max with .NET 10.0.14 and
BenchmarkDotNet 0.15.8. A ratio is NSmithy's time divided by the other stack's,
so below 1 is faster. Ratios compare rows of one report only.

## Where NSmithy is faster

- **Large bodies on the server.** A 1 MB request body takes 0.73× the time of
  the hand-written minimal API and allocates 0.74× as much. A 10,000-item
  response takes 0.81×, a 100-item response 0.84×.
- **JSON deserialization.** The schema codec reads 0.75 to 0.78× as long as
  `System.Text.Json` source generation and allocates 0.85 to 0.91× as much.
- **Against MVC.** The server beats the hand-written MVC controller on every
  scenario except modeled errors, for example 7.4 µs against 11.3 µs for a
  single-item read. The TypeSpec C# server emitter generates MVC controllers;
  on the four scenarios it can serve, NSmithy takes 0.46 to 0.68× its time.
- **Large payloads on the client.** Against NSwag, a 1 MB request takes 0.58×
  the time, a 10,000-item response 0.84×, and a 100-item response 0.93×.
- **The gRPC server** is on par with Grpc.AspNetCore, at 1.01 to 1.06×.

## Where NSmithy is slower

- **Fixed cost per call.** A single-item read on the server takes 1.18× the
  minimal API. On the client it takes 1.33× NSwag and 1.52× the hand-written
  client, which has no retry, interceptors, telemetry, or auth. The gap shrinks
  as payloads grow and reverses on large ones.
- **HTTP bindings.** An operation with six query parameters, four request
  headers, and a response header takes 1.63× the minimal API on the server.
- **Modeled errors.** Handlers throw modeled errors, and the exception makes an
  error response about 2× the minimal API's under Kestrel.
- **JSON serialization** takes 1.06 to 1.18× `System.Text.Json` source
  generation, while allocating 0.82 to 0.87× as much.
- **Client allocations on large responses.** A 10,000-item response allocates
  6.2 MB against NSwag's 3.9 MB, because the generated models are immutable.
- **The gRPC client** takes 1.36 to 1.48× Grpc.Net with Google.Protobuf.

[`FINDINGS.md`](https://github.com/thomaslaich/smithy-dotnet/blob/main/benchmarks/FINDINGS.md)
explains each gap and what would close it, and the
[full reports](https://github.com/thomaslaich/smithy-dotnet/tree/main/benchmarks/results)
cover the CBOR, XML, and Protobuf codecs too.
