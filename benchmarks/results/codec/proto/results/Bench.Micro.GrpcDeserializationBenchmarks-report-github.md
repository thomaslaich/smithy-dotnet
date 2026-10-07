```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                        | Scenario       | Mean        | Error     | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------------- |------------:|----------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf deserialize&#39;** | **get-item**       |    **165.0 ns** |   **2.18 ns** |  **2.04 ns** |  **1.00** |    **0.02** | **0.0687** |      **-** |     **576 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | get-item       |    235.0 ns |   3.71 ns |  3.47 ns |  1.42 |    0.03 | 0.0582 |      - |     488 B |        0.85 |
|                               |                |             |           |          |       |         |        |        |           |             |
| **&#39;Google.Protobuf deserialize&#39;** | **list-items-100** | **14,579.8 ns** |  **66.06 ns** | **58.56 ns** |  **1.00** |    **0.01** | **4.7607** | **0.5341** |   **39936 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | list-items-100 | 22,576.2 ns | 108.33 ns | 96.03 ns |  1.55 |    0.01 | 5.5237 | 0.6409 |   46288 B |        1.16 |
