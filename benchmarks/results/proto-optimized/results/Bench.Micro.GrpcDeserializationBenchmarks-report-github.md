```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                        | Scenario       | Mean        | Error     | StdDev    | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------------- |------------:|----------:|----------:|------:|-------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf deserialize&#39;** | **get-item**       |    **184.9 ns** |   **0.81 ns** |   **0.76 ns** |  **1.00** | **0.0687** |      **-** |     **576 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | get-item       |    333.4 ns |   0.91 ns |   0.85 ns |  1.80 | 0.0849 |      - |     712 B |        1.24 |
|                               |                |             |           |           |       |        |        |           |             |
| **&#39;Google.Protobuf deserialize&#39;** | **list-items-100** | **16,574.5 ns** |  **80.20 ns** |  **71.10 ns** |  **1.00** | **4.7607** | **0.5188** |   **39936 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | list-items-100 | 33,107.4 ns | 136.91 ns | 121.36 ns |  2.00 | 8.9722 | 0.9766 |   75120 B |        1.88 |
