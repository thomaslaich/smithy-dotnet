# Designs

Public design documents for NSmithy. Each document covers a specific area of
the system: its goals, the chosen design, alternatives considered, and
tradeoffs.

## Index

| Document | Area |
| --- | --- |
| [codegen-architecture.md](codegen-architecture.md) | Java plugin + MSBuild integration pipeline |
| [shapes.md](shapes.md) | Smithy shape → C# type mapping |
| [serialization.md](serialization.md) | Schema-aware codec and serialization design |
| [http-interfaces.md](http-interfaces.md) | HTTP transport abstractions |
| [client-architecture.md](client-architecture.md) | Generated client lifecycle, config, interceptors, auth, retries |
| [server-architecture.md](server-architecture.md) | Generated server dispatch, shared server runtime, host adapter |
| [native-grpc.md](native-grpc.md) | Native proto codec + gRPC protocol (no protoc/Grpc.Tools) |
| [streaming.md](streaming.md) | Event streams and streaming blob payloads |

## Background

Smithy code generators are typically structured as a repository containing a
Java plugin (published to Maven Central), runtime libraries for the target
language, and build tooling that connects the two. See
[Creating a Codegen Repo](https://smithy.io/2.0/guides/building-codegen/creating-codegen-repo.html)
for the recommended layout.

NSmithy follows this pattern:

- `codegen/smithy-csharp-codegen` — Java Smithy plugin (Gradle, published to Maven Central)
- `packages/` — .NET runtime libraries (NuGet)
- `packages/NSmithy.MSBuild` — MSBuild integration that connects the two
