```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                      | Scenario       | Mean        | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |--------------- |------------:|---------:|---------:|------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf serialize&#39;** | **get-item**       |    **114.4 ns** |  **0.37 ns** |  **0.33 ns** |  **1.00** | **0.0181** |     **152 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | get-item       |    134.9 ns |  0.79 ns |  0.66 ns |  1.18 | 0.0105 |      88 B |        0.58 |
|                             |                |             |          |          |       |        |           |             |
| **&#39;Google.Protobuf serialize&#39;** | **list-items-100** | **10,522.5 ns** | **64.67 ns** | **60.49 ns** |  **1.00** | **0.7019** |    **5968 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | list-items-100 | 11,477.3 ns | 77.59 ns | 64.79 ns |  1.09 | 0.7019 |    5904 B |        0.99 |
