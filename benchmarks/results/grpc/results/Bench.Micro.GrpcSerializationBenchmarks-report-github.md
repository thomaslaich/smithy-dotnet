```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                      | Scenario       | Mean        | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |--------------- |------------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf serialize&#39;** | **get-item**       |    **128.5 ns** |   **1.21 ns** |  **1.13 ns** |  **1.00** |    **0.01** | **0.0181** |      **-** |     **152 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | get-item       |    229.7 ns |   1.24 ns |  1.16 ns |  1.79 |    0.02 | 0.0734 |      - |     616 B |        4.05 |
|                             |                |             |           |          |       |         |        |        |           |             |
| **&#39;Google.Protobuf serialize&#39;** | **list-items-100** | **11,801.8 ns** |  **88.10 ns** | **82.41 ns** |  **1.00** |    **0.01** | **0.7019** |      **-** |    **5968 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | list-items-100 | 22,626.0 ns | 100.84 ns | 94.33 ns |  1.92 |    0.02 | 7.7209 | 0.2441 |   64720 B |       10.84 |
