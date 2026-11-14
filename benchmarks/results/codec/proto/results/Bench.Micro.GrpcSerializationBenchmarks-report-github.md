```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                      | Scenario       | Mean        | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |--------------- |------------:|---------:|---------:|------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf serialize&#39;** | **get-item**       |    **114.3 ns** |  **0.58 ns** |  **0.54 ns** |  **1.00** | **0.0181** |     **152 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | get-item       |    127.2 ns |  0.98 ns |  0.92 ns |  1.11 | 0.0105 |      88 B |        0.58 |
|                             |                |             |          |          |       |        |           |             |
| **&#39;Google.Protobuf serialize&#39;** | **list-items-100** | **10,631.9 ns** | **63.47 ns** | **59.37 ns** |  **1.00** | **0.7019** |    **5968 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | list-items-100 | 10,693.9 ns | 44.06 ns | 41.21 ns |  1.01 | 0.7019 |    5904 B |        0.99 |
