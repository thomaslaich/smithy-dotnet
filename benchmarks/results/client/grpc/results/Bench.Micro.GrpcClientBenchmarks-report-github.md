```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                | Scenario       | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------- |--------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.Net client&#39;**     | **get-item**       |  **1.192 μs** | **0.0159 μs** | **0.0149 μs** |  **1.00** |    **0.02** | **0.3986** | **0.0038** |   **3.26 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | get-item       |  1.507 μs | 0.0238 μs | 0.0223 μs |  1.26 |    0.02 | 0.5856 | 0.0038 |   4.79 KB |        1.47 |
|                       |                |           |           |           |       |         |        |        |           |             |
| **&#39;Grpc.Net client&#39;**     | **list-items-100** | **18.086 μs** | **0.1064 μs** | **0.0996 μs** |  **1.00** |    **0.01** | **5.0964** | **0.6104** |  **41.68 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | list-items-100 | 25.681 μs | 0.3676 μs | 0.3439 μs |  1.42 |    0.02 | 8.5144 | 1.0986 |  69.71 KB |        1.67 |
