```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                   | Scenario       | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |--------------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.AspNetCore server&#39;** | **get-item**       | **10.51 μs** | **0.146 μs** | **0.122 μs** |  **1.00** |    **0.02** | **1.5259** | **0.0305** |  **12.38 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | get-item       | 12.86 μs | 0.156 μs | 0.138 μs |  1.22 |    0.02 | 1.8158 | 0.0610 |  14.75 KB |        1.19 |
|                          |                |          |          |          |       |         |        |        |           |             |
| **&#39;Grpc.AspNetCore server&#39;** | **list-items-100** | **29.47 μs** | **0.549 μs** | **0.514 μs** |  **1.00** |    **0.02** | **3.0212** | **0.1221** |  **23.92 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | list-items-100 | 41.52 μs | 0.150 μs | 0.133 μs |  1.41 |    0.03 | 5.2490 | 0.1831 |  40.76 KB |        1.70 |
