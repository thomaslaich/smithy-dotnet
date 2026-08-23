```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                   | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| &#39;success response&#39;       | 294.8 ns | 1.75 ns | 1.63 ns |  1.00 | 0.1211 |    1016 B |        1.00 |
| &#39;modeled error response&#39; | 309.1 ns | 1.80 ns | 1.69 ns |  1.05 | 0.1497 |    1256 B |        1.24 |
