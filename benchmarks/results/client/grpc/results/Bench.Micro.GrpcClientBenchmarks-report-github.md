```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                | Scenario       | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------- |--------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.Net client&#39;**     | **get-item**       |  **1.152 μs** | **0.0166 μs** | **0.0156 μs** |  **1.00** |    **0.02** | **0.3986** | **0.0038** |   **3.26 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | get-item       |  1.565 μs | 0.0116 μs | 0.0109 μs |  1.36 |    0.02 | 0.6218 | 0.0038 |   5.09 KB |        1.56 |
|                       |                |           |           |           |       |         |        |        |           |             |
| **&#39;Grpc.Net client&#39;**     | **list-items-100** | **16.755 μs** | **0.1942 μs** | **0.1721 μs** |  **1.00** |    **0.01** | **5.0964** | **0.6104** |  **41.68 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | list-items-100 | 24.717 μs | 0.2585 μs | 0.2291 μs |  1.48 |    0.02 | 7.2937 | 0.9460 |  59.63 KB |        1.43 |
