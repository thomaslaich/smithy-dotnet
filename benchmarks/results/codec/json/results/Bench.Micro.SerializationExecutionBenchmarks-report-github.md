```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                          | ItemCount | Mean           | Error        | StdDev       | Ratio | Gen0    | Allocated | Alloc Ratio |
|-------------------------------- |---------- |---------------:|-------------:|-------------:|------:|--------:|----------:|------------:|
| **&#39;STJ source-gen execution&#39;**      | **1**         |       **206.9 ns** |      **1.48 ns** |      **1.39 ns** |  **1.00** |  **0.0076** |      **64 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 1         |       249.8 ns |      1.58 ns |      1.48 ns |  1.21 |  0.0076 |      64 B |        1.00 |
| &#39;NSmithy handwritten execution&#39; | 1         |       205.1 ns |      0.99 ns |      0.93 ns |  0.99 |  0.0076 |      64 B |        1.00 |
|                                 |           |                |              |              |       |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **100**       |    **16,763.4 ns** |     **98.34 ns** |     **91.99 ns** |  **1.00** |  **0.3662** |    **3232 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 100       |    20,816.7 ns |    110.15 ns |    103.03 ns |  1.24 |  0.3662 |    3232 B |        1.00 |
| &#39;NSmithy handwritten execution&#39; | 100       |    16,855.0 ns |    247.80 ns |    231.79 ns |  1.01 |  0.3662 |    3232 B |        1.00 |
|                                 |           |                |              |              |       |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **10000**     | **1,656,251.3 ns** | **14,800.88 ns** | **13,844.75 ns** |  **1.00** | **37.1094** |  **320046 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 10000     | 2,092,851.0 ns | 13,780.70 ns | 12,890.48 ns |  1.26 | 35.1563 |  320059 B |        1.00 |
| &#39;NSmithy handwritten execution&#39; | 10000     | 1,675,740.2 ns |  5,465.59 ns |  5,112.51 ns |  1.01 | 37.1094 |  320045 B |        1.00 |
