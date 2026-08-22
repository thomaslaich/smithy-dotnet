```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                   | Scenario       | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------- |--------------- |---------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| **&#39;Grpc.AspNetCore server&#39;** | **get-item**       | **10.20 μs** | **0.165 μs** | **0.170 μs** |  **1.00** |    **0.02** |  **1.5259** | **0.0458** |  **12.39 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | get-item       | 12.38 μs | 0.247 μs | 0.413 μs |  1.21 |    0.04 |  1.9073 | 0.0610 |  15.45 KB |        1.25 |
|                          |                |          |          |          |       |         |         |        |           |             |
| **&#39;Grpc.AspNetCore server&#39;** | **list-items-100** | **29.96 μs** | **0.338 μs** | **0.317 μs** |  **1.00** |    **0.01** |  **2.9907** | **0.1221** |  **23.96 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | list-items-100 | 45.04 μs | 0.892 μs | 0.835 μs |  1.50 |    0.03 | 11.9019 | 0.6714 |   95.3 KB |        3.98 |
