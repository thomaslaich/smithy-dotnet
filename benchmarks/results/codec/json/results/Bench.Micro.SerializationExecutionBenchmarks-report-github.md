```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                          | ItemCount | Mean           | Error       | StdDev      | Ratio | Gen0    | Allocated | Alloc Ratio |
|-------------------------------- |---------- |---------------:|------------:|------------:|------:|--------:|----------:|------------:|
| **&#39;STJ source-gen execution&#39;**      | **1**         |       **184.2 ns** |     **0.89 ns** |     **0.83 ns** |  **1.00** |  **0.0076** |      **64 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 1         |       181.8 ns |     1.12 ns |     1.05 ns |  0.99 |       - |         - |        0.00 |
| &#39;NSmithy handwritten execution&#39; | 1         |       179.3 ns |     0.61 ns |     0.57 ns |  0.97 |  0.0076 |      64 B |        1.00 |
|                                 |           |                |             |             |       |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **100**       |    **15,309.2 ns** |    **57.65 ns** |    **53.92 ns** |  **1.00** |  **0.3662** |    **3232 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 100       |    16,223.5 ns |   104.98 ns |    98.20 ns |  1.06 |       - |         - |        0.00 |
| &#39;NSmithy handwritten execution&#39; | 100       |    15,481.3 ns |    76.63 ns |    67.93 ns |  1.01 |  0.3662 |    3232 B |        1.00 |
|                                 |           |                |             |             |       |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **10000**     | **1,522,805.4 ns** | **4,157.26 ns** | **3,888.71 ns** |  **1.00** | **37.1094** |  **320044 B** |       **1.000** |
| &#39;NSmithy schema execution&#39;      | 10000     | 1,610,723.3 ns | 8,731.85 ns | 8,167.78 ns |  1.06 |       - |      12 B |       0.000 |
| &#39;NSmithy handwritten execution&#39; | 10000     | 1,532,442.1 ns | 3,690.94 ns | 3,271.93 ns |  1.01 | 37.1094 |  320044 B |       1.000 |
