```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                      | Scenario       | Mean        | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |--------------- |------------:|----------:|----------:|------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf serialize&#39;** | **get-item**       |    **127.9 ns** |   **0.74 ns** |   **0.69 ns** |  **1.00** | **0.0181** |     **152 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | get-item       |    217.6 ns |   0.73 ns |   0.69 ns |  1.70 | 0.0143 |     120 B |        0.79 |
|                             |                |             |           |           |       |        |           |             |
| **&#39;Google.Protobuf serialize&#39;** | **list-items-100** | **11,838.5 ns** |  **28.55 ns** |  **25.31 ns** |  **1.00** | **0.7019** |    **5968 B** |        **1.00** |
| &#39;NSmithy Proto serialize&#39;   | list-items-100 | 19,727.4 ns | 138.12 ns | 129.20 ns |  1.67 | 1.0681 |    9136 B |        1.53 |
