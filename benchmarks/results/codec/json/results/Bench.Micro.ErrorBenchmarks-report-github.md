```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                   | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| &#39;success response&#39;       | 242.9 ns | 1.37 ns | 1.28 ns |  1.00 | 0.1392 |   1.14 KB |        1.00 |
| &#39;modeled error response&#39; | 281.0 ns | 1.15 ns | 1.07 ns |  1.16 | 0.1488 |   1.22 KB |        1.07 |
