```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                        | Scenario       | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------------- |------------:|----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf deserialize&#39;** | **get-item**       |    **183.2 ns** |   **0.98 ns** |   **0.92 ns** |  **1.00** |    **0.01** |  **0.0687** |      **-** |     **576 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | get-item       |    498.1 ns |   3.53 ns |   3.30 ns |  2.72 |    0.02 |  0.1745 |      - |    1464 B |        2.54 |
|                               |                |             |           |           |       |         |         |        |           |             |
| **&#39;Google.Protobuf deserialize&#39;** | **list-items-100** | **16,407.3 ns** |  **57.81 ns** |  **54.08 ns** |  **1.00** |    **0.00** |  **4.7607** | **0.5188** |   **39936 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | list-items-100 | 47,715.5 ns | 276.83 ns | 258.95 ns |  2.91 |    0.02 | 15.5029 | 1.7090 |  130032 B |        3.26 |
