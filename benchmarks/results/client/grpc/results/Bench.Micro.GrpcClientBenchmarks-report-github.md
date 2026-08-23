```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                | Scenario       | Mean      | Error     | StdDev    | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------- |--------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **&#39;Grpc.Net client&#39;**     | **get-item**       |  **1.189 μs** | **0.0042 μs** | **0.0039 μs** |  **1.00** |  **0.3986** | **0.0038** |   **3.26 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | get-item       |  1.611 μs | 0.0081 μs | 0.0076 μs |  1.35 |  0.6084 | 0.0038 |   4.98 KB |        1.53 |
|                       |                |           |           |           |       |         |        |           |             |
| **&#39;Grpc.Net client&#39;**     | **list-items-100** | **18.054 μs** | **0.0587 μs** | **0.0549 μs** |  **1.00** |  **5.0964** | **0.6104** |  **41.68 KB** |        **1.00** |
| &#39;NSmithy gRPC client&#39; | list-items-100 | 35.792 μs | 0.1244 μs | 0.1163 μs |  1.98 | 11.5356 | 1.4038 |  94.71 KB |        2.27 |
