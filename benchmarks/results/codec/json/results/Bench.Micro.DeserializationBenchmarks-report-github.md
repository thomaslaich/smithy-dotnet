```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                 | Scenario           | Mean         | Error      | StdDev      | Ratio | RatioSD | Gen0     | Gen1    | Gen2   | Allocated  | Alloc Ratio |
|----------------------- |------------------- |-------------:|-----------:|------------:|------:|--------:|---------:|--------:|-------:|-----------:|------------:|
| **&#39;STJ source-gen&#39;**       | **create-order-large** | **3,770.781 μs** | **29.4051 μs** |  **27.5055 μs** |  **1.00** |    **0.01** | **105.4688** | **46.8750** |      **-** | **4630.21 KB** |        **1.00** |
| &#39;NSmithy schema codec&#39; | create-order-large | 4,144.537 μs | 82.7612 μs | 110.4838 μs |  1.10 |    0.03 | 101.5625 | 70.3125 | 7.8125 | 4361.83 KB |        0.94 |
|                        |                    |              |            |             |       |         |          |         |        |            |             |
| **&#39;STJ source-gen&#39;**       | **create-order-small** |     **4.230 μs** |  **0.0321 μs** |   **0.0300 μs** |  **1.00** |    **0.01** |   **0.6866** |  **0.0076** |      **-** |    **5.63 KB** |        **1.00** |
| &#39;NSmithy schema codec&#39; | create-order-small |     4.590 μs |  0.0231 μs |   0.0205 μs |  1.09 |    0.01 |   0.6027 |  0.0076 |      - |    4.94 KB |        0.88 |
