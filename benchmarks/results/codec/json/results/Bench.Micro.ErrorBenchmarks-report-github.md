```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method                   | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| &#39;success response&#39;       | 283.3 ns | 1.51 ns | 1.41 ns |  1.00 | 0.1211 |    1016 B |        1.00 |
| &#39;modeled error response&#39; | 309.9 ns | 2.01 ns | 1.88 ns |  1.09 | 0.1497 |    1256 B |        1.24 |
