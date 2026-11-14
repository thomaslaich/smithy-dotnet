```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                          | ItemCount | Mean           | Error       | StdDev      | Ratio | Gen0    | Allocated | Alloc Ratio |
|-------------------------------- |---------- |---------------:|------------:|------------:|------:|--------:|----------:|------------:|
| **&#39;STJ source-gen execution&#39;**      | **1**         |       **187.1 ns** |     **1.88 ns** |     **1.76 ns** |  **1.00** |  **0.0076** |      **64 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 1         |       182.2 ns |     0.74 ns |     0.69 ns |  0.97 |       - |         - |        0.00 |
| &#39;NSmithy handwritten execution&#39; | 1         |       180.0 ns |     0.49 ns |     0.46 ns |  0.96 |  0.0076 |      64 B |        1.00 |
|                                 |           |                |             |             |       |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **100**       |    **15,041.8 ns** |    **45.76 ns** |    **40.57 ns** |  **1.00** |  **0.3815** |    **3232 B** |        **1.00** |
| &#39;NSmithy schema execution&#39;      | 100       |    15,701.4 ns |    50.66 ns |    47.38 ns |  1.04 |       - |         - |        0.00 |
| &#39;NSmithy handwritten execution&#39; | 100       |    14,967.8 ns |    34.84 ns |    32.59 ns |  1.00 |  0.3815 |    3232 B |        1.00 |
|                                 |           |                |             |             |       |         |           |             |
| **&#39;STJ source-gen execution&#39;**      | **10000**     | **1,491,901.3 ns** | **6,520.80 ns** | **5,780.51 ns** |  **1.00** | **37.1094** |  **320044 B** |       **1.000** |
| &#39;NSmithy schema execution&#39;      | 10000     | 1,557,179.5 ns | 6,581.53 ns | 5,834.35 ns |  1.04 |       - |      12 B |       0.000 |
| &#39;NSmithy handwritten execution&#39; | 10000     | 1,483,033.5 ns | 4,983.55 ns | 4,417.78 ns |  0.99 | 37.1094 |  320044 B |       1.000 |
