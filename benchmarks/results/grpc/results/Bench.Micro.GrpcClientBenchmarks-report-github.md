```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                | Scenario       | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------- |--------------- |----------:|----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| **&#39;Grpc.Net client&#39;**     | **get-item**       |  **1.188 μs** | **0.0049 μs** | **0.0046 μs** |  **1.00** |    **0.01** |  **0.3986** | **0.0038** |   **3.26 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | get-item       |  1.792 μs | 0.0095 μs | 0.0088 μs |  1.51 |    0.01 |  0.7172 | 0.0057 |   5.86 KB |        1.80 |
|                       |                |           |           |           |       |         |         |        |           |             |
| **&#39;Grpc.Net client&#39;**     | **list-items-100** | **17.781 μs** | **0.0951 μs** | **0.0890 μs** |  **1.00** |    **0.01** |  **5.0964** | **0.6104** |  **41.68 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | list-items-100 | 50.765 μs | 0.2055 μs | 0.1821 μs |  2.86 |    0.02 | 18.1274 | 2.4414 | 148.45 KB |        3.56 |
