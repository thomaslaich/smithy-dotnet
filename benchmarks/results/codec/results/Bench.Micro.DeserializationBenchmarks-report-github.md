```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                 | Scenario           | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------- |------------------- |-------------:|-----------:|-----------:|------:|--------:|---------:|--------:|-----------:|------------:|
| **&#39;STJ source-gen&#39;**       | **create-order-large** | **3,775.174 μs** | **46.6161 μs** | **43.6048 μs** |  **1.00** |    **0.02** | **113.2813** | **54.6875** | **4630.18 KB** |        **1.00** |
| &#39;NSmithy schema codec&#39; | create-order-large | 4,162.480 μs | 68.9447 μs | 64.4909 μs |  1.10 |    0.02 | 117.1875 | 54.6875 | 5729.98 KB |        1.24 |
|                        |                    |              |            |            |       |         |          |         |            |             |
| **&#39;STJ source-gen&#39;**       | **create-order-small** |     **4.187 μs** |  **0.0557 μs** |  **0.0521 μs** |  **1.00** |    **0.02** |   **0.6866** |  **0.0076** |    **5.63 KB** |        **1.00** |
| &#39;NSmithy schema codec&#39; | create-order-small |     4.811 μs |  0.0354 μs |  0.0332 μs |  1.15 |    0.02 |   0.7629 |  0.0076 |    6.27 KB |        1.11 |
