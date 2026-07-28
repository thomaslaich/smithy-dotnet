---
title: Design Docs
description: Links to the NSmithy design documents on GitHub.
---

The NSmithy design documents live in the
[`designs/`](https://github.com/thomaslaich/smithy-dotnet/tree/main/designs)
folder of the repository. Each document covers a specific area of the system:
its goals, the chosen design, alternatives considered, and tradeoffs.

| Document | Area |
| --- | --- |
| [codegen-architecture.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/codegen-architecture.md) | Java plugin + MSBuild integration pipeline |
| [shapes.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/shapes.md) | Smithy shape → C# type mapping |
| [serialization.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/serialization.md) | Schema-aware codec and serialization design |
| [http-interfaces.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/http-interfaces.md) | HTTP transport abstractions |
| [client-architecture.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/client-architecture.md) | Generated client lifecycle, config, interceptors, auth, retries |
| [server-architecture.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/server-architecture.md) | Generated server dispatch, shared server runtime, host adapter |
| [native-grpc.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/native-grpc.md) | Native gRPC: own proto codec + gRPC protocol (no protoc/Grpc.Net) |
| [streaming.md](https://github.com/thomaslaich/smithy-dotnet/blob/main/designs/streaming.md) | Event streams and streaming blob payloads |
