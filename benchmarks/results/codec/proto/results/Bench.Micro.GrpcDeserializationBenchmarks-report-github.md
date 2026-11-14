```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                        | Scenario       | Mean        | Error    | StdDev   | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------------- |------------:|---------:|---------:|------:|-------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf deserialize&#39;** | **get-item**       |    **166.4 ns** |  **0.56 ns** |  **0.52 ns** |  **1.00** | **0.0687** |      **-** |     **576 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | get-item       |    223.2 ns |  1.17 ns |  1.09 ns |  1.34 | 0.0572 |      - |     480 B |        0.83 |
|                               |                |             |          |          |       |        |        |           |             |
| **&#39;Google.Protobuf deserialize&#39;** | **list-items-100** | **14,538.1 ns** | **51.47 ns** | **48.15 ns** |  **1.00** | **4.7607** | **0.5341** |   **39936 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | list-items-100 | 21,369.7 ns | 45.63 ns | 42.68 ns |  1.47 | 5.3406 | 0.5798 |   44672 B |        1.12 |
