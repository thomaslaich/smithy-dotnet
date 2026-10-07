```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                          | ItemCount | Mean           | Error       | StdDev      | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|-------------------------------- |---------- |---------------:|------------:|------------:|------:|--------:|--------:|----------:|------------:|
| **&#39;STJ source-gen execution&#39;**      | **1**         |       **181.7 ns** |     **1.04 ns** |     **0.97 ns** |  **1.00** |    **0.01** |  **0.0076** |      **64 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 1         |       206.2 ns |     4.04 ns |     3.58 ns |  1.13 |    0.02 |       - |         - |        0.00 |
| &#39;NSmithy handwritten execution&#39; | 1         |       185.2 ns |     2.68 ns |     2.37 ns |  1.02 |    0.01 |  0.0076 |      64 B |        1.00 |
|                                 |           |                |             |             |       |         |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **100**       |    **15,423.0 ns** |    **42.55 ns** |    **35.53 ns** |  **1.00** |    **0.00** |  **0.3662** |    **3232 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 100       |    17,628.5 ns |    68.12 ns |    63.72 ns |  1.14 |    0.00 |       - |         - |        0.00 |
| &#39;NSmithy handwritten execution&#39; | 100       |    15,459.8 ns |    35.86 ns |    29.95 ns |  1.00 |    0.00 |  0.3662 |    3232 B |        1.00 |
|                                 |           |                |             |             |       |         |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **10000**     | **1,528,025.9 ns** | **7,908.75 ns** | **7,397.85 ns** |  **1.00** |    **0.01** | **37.1094** |  **320044 B** |       **1.000** |
| &#39;NSmithy schema execution&#39;      | 10000     | 1,774,026.9 ns | 4,072.67 ns | 3,610.32 ns |  1.16 |    0.01 |       - |      12 B |       0.000 |
| &#39;NSmithy handwritten execution&#39; | 10000     | 1,525,307.8 ns | 3,634.37 ns | 3,221.77 ns |  1.00 |    0.01 | 37.1094 |  320044 B |       1.000 |
