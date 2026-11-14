```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                   | Scenario       | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |--------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.AspNetCore server&#39;** | **get-item**       |  **7.930 μs** | **0.1464 μs** | **0.1298 μs** |  **1.00** |    **0.02** | **1.5259** | **0.0458** |  **12.38 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | get-item       |  8.428 μs | 0.1671 μs | 0.1482 μs |  1.06 |    0.02 | 1.6327 | 0.0458 |  13.35 KB |        1.08 |
|                          |                |           |           |           |       |         |        |        |           |             |
| **&#39;Grpc.AspNetCore server&#39;** | **list-items-100** | **28.322 μs** | **0.3123 μs** | **0.2769 μs** |  **1.00** |    **0.01** | **3.0212** | **0.0916** |  **23.92 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | list-items-100 | 28.640 μs | 0.3280 μs | 0.3068 μs |  1.01 |    0.01 | 3.4180 | 0.1221 |  24.88 KB |        1.04 |
