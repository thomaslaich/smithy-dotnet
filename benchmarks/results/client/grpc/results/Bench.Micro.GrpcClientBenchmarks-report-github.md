```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                | Scenario       | Mean      | Error     | StdDev    | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------- |--------------- |----------:|----------:|----------:|------:|-------:|-------:|----------:|------------:|
| **&#39;Grpc.Net client&#39;**     | **get-item**       |  **1.118 μs** | **0.0066 μs** | **0.0058 μs** |  **1.00** | **0.3986** | **0.0038** |   **3.26 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | get-item       |  1.515 μs | 0.0069 μs | 0.0064 μs |  1.35 | 0.6237 | 0.0038 |    5.1 KB |        1.57 |
|                       |                |           |           |           |       |        |        |           |             |
| **&#39;Grpc.Net client&#39;**     | **list-items-100** | **15.953 μs** | **0.0955 μs** | **0.0893 μs** |  **1.00** | **5.0964** | **0.6104** |  **41.68 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | list-items-100 | 23.578 μs | 0.0753 μs | 0.0588 μs |  1.48 | 7.4768 | 0.8240 |  61.21 KB |        1.47 |
