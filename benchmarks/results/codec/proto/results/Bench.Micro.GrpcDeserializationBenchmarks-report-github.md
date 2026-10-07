```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                        | Scenario       | Mean        | Error    | StdDev   | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------------- |------------:|---------:|---------:|------:|-------:|-------:|----------:|------------:|
| **&#39;Google.Protobuf deserialize&#39;** | **get-item**       |    **165.3 ns** |  **0.45 ns** |  **0.40 ns** |  **1.00** | **0.0687** |      **-** |     **576 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | get-item       |    219.9 ns |  0.74 ns |  0.69 ns |  1.33 | 0.0582 |      - |     488 B |        0.85 |
|                               |                |             |          |          |       |        |        |           |             |
| **&#39;Google.Protobuf deserialize&#39;** | **list-items-100** | **14,569.6 ns** | **78.87 ns** | **69.91 ns** |  **1.00** | **4.7607** | **0.5341** |   **39936 B** |        **1.00** |
| &#39;NSmithy Proto deserialize&#39;   | list-items-100 | 20,863.8 ns | 73.68 ns | 65.32 ns |  1.43 | 5.5237 | 0.6409 |   46288 B |        1.16 |
