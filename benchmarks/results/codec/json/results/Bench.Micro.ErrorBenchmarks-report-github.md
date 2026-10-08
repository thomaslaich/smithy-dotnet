```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                   | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| &#39;success response&#39;       | 257.8 ns | 0.83 ns | 0.73 ns |  1.00 | 0.1326 |   1.09 KB |        1.00 |
| &#39;modeled error response&#39; | 288.6 ns | 0.96 ns | 0.75 ns |  1.12 | 0.1612 |   1.32 KB |        1.22 |
