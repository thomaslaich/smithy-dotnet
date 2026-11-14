```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method    | Mean     | Error    | StdDev   | Gen0   | Allocated |
|---------- |---------:|---------:|---------:|-------:|----------:|
| Serialize | 39.86 ns | 0.096 ns | 0.090 ns | 0.0057 |      48 B |
