```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                | Scenario       | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------- |--------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.Net client&#39;**     | **get-item**       |  **1.147 μs** | **0.0054 μs** | **0.0048 μs** |  **1.00** |    **0.01** | **0.3986** | **0.0038** |   **3.26 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | get-item       |  1.562 μs | 0.0048 μs | 0.0045 μs |  1.36 |    0.01 | 0.6237 | 0.0038 |    5.1 KB |        1.57 |
|                       |                |           |           |           |       |         |        |        |           |             |
| **&#39;Grpc.Net client&#39;**     | **list-items-100** | **16.029 μs** | **0.0796 μs** | **0.0664 μs** |  **1.00** |    **0.01** | **5.0964** | **0.6104** |  **41.68 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | list-items-100 | 24.988 μs | 0.3978 μs | 0.4257 μs |  1.56 |    0.03 | 7.4768 | 0.8240 |  61.21 KB |        1.47 |
