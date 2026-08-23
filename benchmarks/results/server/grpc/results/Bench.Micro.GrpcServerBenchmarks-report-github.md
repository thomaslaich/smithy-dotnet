```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                   | Scenario       | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |--------------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.AspNetCore server&#39;** | **get-item**       | **13.07 μs** | **0.257 μs** | **0.325 μs** |  **1.00** |    **0.04** | **1.5259** | **0.0458** |  **12.38 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | get-item       | 14.82 μs | 0.204 μs | 0.191 μs |  1.13 |    0.03 | 1.8158 | 0.0458 |  14.75 KB |        1.19 |
|                          |                |          |          |          |       |         |        |        |           |             |
| **&#39;Grpc.AspNetCore server&#39;** | **list-items-100** | **32.24 μs** | **0.611 μs** | **0.628 μs** |  **1.00** |    **0.03** | **2.9907** | **0.0610** |  **23.91 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | list-items-100 | 39.09 μs | 0.465 μs | 0.389 μs |  1.21 |    0.03 | 5.2490 | 0.1831 |  40.75 KB |        1.70 |
