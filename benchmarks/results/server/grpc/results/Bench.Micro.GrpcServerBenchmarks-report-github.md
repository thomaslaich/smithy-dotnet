```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                   | Scenario       | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |--------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.AspNetCore server&#39;** | **get-item**       |  **8.285 μs** | **0.1585 μs** | **0.1238 μs** |  **1.00** |    **0.02** | **1.5259** | **0.0458** |  **12.38 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | get-item       |  9.427 μs | 0.1115 μs | 0.0931 μs |  1.14 |    0.02 | 1.8158 | 0.0610 |  14.72 KB |        1.19 |
|                          |                |           |           |           |       |         |        |        |           |             |
| **&#39;Grpc.AspNetCore server&#39;** | **list-items-100** | **28.831 μs** | **0.2007 μs** | **0.1877 μs** |  **1.00** |    **0.01** | **3.0518** | **0.0916** |  **23.92 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | list-items-100 | 32.479 μs | 0.1820 μs | 0.1614 μs |  1.13 |    0.01 | 4.8828 | 0.1831 |  37.61 KB |        1.57 |
