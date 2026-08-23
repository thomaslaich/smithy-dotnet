```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                      | Scenario       | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |--------------- |------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf serialize&#39;** | **get-item**       |    **123.3 ns** |   **1.19 ns** |   **1.11 ns** |  **1.00** |    **0.01** | **0.0181** |     **152 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | get-item       |    150.4 ns |   1.08 ns |   0.95 ns |  1.22 |    0.01 | 0.0143 |     120 B |        0.79 |
|                             |                |             |           |           |       |         |        |           |             |
| **&#39;Google.Protobuf serialize&#39;** | **list-items-100** | **11,183.3 ns** | **116.40 ns** | **103.19 ns** |  **1.00** |    **0.01** | **0.7019** |    **5968 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | list-items-100 | 13,890.3 ns | 273.51 ns | 355.64 ns |  1.24 |    0.03 | 1.0834 |    9136 B |        1.53 |
