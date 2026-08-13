```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                 | Scenario           | Mean         | Error      | StdDev      | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|----------------------- |------------------- |-------------:|-----------:|------------:|------:|--------:|---------:|--------:|-----------:|------------:|
| **&#39;STJ source-gen&#39;**       | **create-order-large** | **3,536.722 μs** | **39.9725 μs** |  **37.3903 μs** |  **1.00** |    **0.01** |  **97.6563** | **46.8750** | **4630.18 KB** |        **1.00** |
| &#39;NSmithy schema codec&#39; | create-order-large | 4,168.920 μs | 82.7521 μs | 110.4717 μs |  1.18 |    0.03 | 101.5625 | 46.8750 | 5729.98 KB |        1.24 |
|                        |                    |              |            |             |       |         |          |         |            |             |
| **&#39;STJ source-gen&#39;**       | **create-order-small** |     **3.971 μs** |  **0.0377 μs** |   **0.0352 μs** |  **1.00** |    **0.01** |   **0.6866** |  **0.0076** |    **5.63 KB** |        **1.00** |
| &#39;NSmithy schema codec&#39; | create-order-small |     4.631 μs |  0.0267 μs |   0.0223 μs |  1.17 |    0.01 |   0.7629 |  0.0076 |    6.27 KB |        1.11 |
