```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                        | Scenario       | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------------- |------------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf deserialize&#39;** | **get-item**       |    **174.2 ns** |   **0.71 ns** |   **0.60 ns** |  **1.00** |    **0.00** | **0.0687** |      **-** |     **576 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | get-item       |    229.0 ns |   1.97 ns |   1.85 ns |  1.31 |    0.01 | 0.0620 |      - |     520 B |        0.90 |
|                               |                |             |           |           |       |         |        |        |           |             |
| **&#39;Google.Protobuf deserialize&#39;** | **list-items-100** | **15,369.5 ns** | **130.43 ns** | **115.62 ns** |  **1.00** |    **0.01** | **4.7607** | **0.5341** |   **39936 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | list-items-100 | 21,152.3 ns | 197.94 ns | 185.15 ns |  1.38 |    0.02 | 5.9204 | 0.6714 |   49520 B |        1.24 |
