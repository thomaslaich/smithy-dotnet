```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Unknown processor
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method    | Mean     | Error    | StdDev   | Gen0   | Allocated |
|---------- |---------:|---------:|---------:|-------:|----------:|
| Serialize | 69.61 ns | 0.396 ns | 0.371 ns | 0.0057 |      48 B |
