```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                          | ItemCount | Mean           | Error        | StdDev       | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|-------------------------------- |---------- |---------------:|-------------:|-------------:|------:|--------:|--------:|----------:|------------:|
| **&#39;STJ source-gen execution&#39;**      | **1**         |       **203.7 ns** |      **1.90 ns** |      **1.77 ns** |  **1.00** |    **0.01** |  **0.0076** |      **64 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 1         |       244.5 ns |      1.92 ns |      1.80 ns |  1.20 |    0.01 |       - |         - |        0.00 |
| &#39;NSmithy handwritten execution&#39; | 1         |       203.5 ns |      0.66 ns |      0.58 ns |  1.00 |    0.01 |  0.0076 |      64 B |        1.00 |
|                                 |           |                |              |              |       |         |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **100**       |    **16,991.9 ns** |     **91.02 ns** |     **85.14 ns** |  **1.00** |    **0.01** |  **0.3662** |    **3232 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 100       |    20,928.3 ns |    278.34 ns |    260.36 ns |  1.23 |    0.02 |       - |         - |        0.00 |
| &#39;NSmithy handwritten execution&#39; | 100       |    17,103.6 ns |     74.05 ns |     69.26 ns |  1.01 |    0.01 |  0.3662 |    3232 B |        1.00 |
|                                 |           |                |              |              |       |         |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **10000**     | **1,678,478.5 ns** |  **4,743.00 ns** |  **4,436.60 ns** |  **1.00** |    **0.00** | **37.1094** |  **320044 B** |       **1.000** |
| &#39;NSmithy schema execution&#39;      | 10000     | 2,129,589.1 ns |  5,611.91 ns |  5,249.38 ns |  1.27 |    0.00 |       - |      25 B |       0.000 |
| &#39;NSmithy handwritten execution&#39; | 10000     | 1,678,393.9 ns | 10,966.09 ns | 10,257.69 ns |  1.00 |    0.01 | 37.1094 |  320044 B |       1.000 |
