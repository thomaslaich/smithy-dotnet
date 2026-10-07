```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                   | Scenario       | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------- |--------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.AspNetCore server&#39;** | **get-item**       |  **7.944 μs** | **0.1411 μs** | **0.1733 μs** |  **1.00** |    **0.03** | **1.5259** | **0.0305** |  **12.38 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | get-item       |  8.849 μs | 0.1596 μs | 0.3401 μs |  1.11 |    0.05 | 1.8158 | 0.0610 |  14.72 KB |        1.19 |
|                          |                |           |           |           |       |         |        |        |           |             |
| **&#39;Grpc.AspNetCore server&#39;** | **list-items-100** | **28.190 μs** | **0.2035 μs** | **0.1804 μs** |  **1.00** |    **0.01** | **3.0212** | **0.0916** |  **23.92 KB** |        **1.00** |
| &#39;NSmithy gRPC server&#39;    | list-items-100 | 31.728 μs | 0.1990 μs | 0.1662 μs |  1.13 |    0.01 | 4.8828 | 0.1831 |  37.61 KB |        1.57 |
