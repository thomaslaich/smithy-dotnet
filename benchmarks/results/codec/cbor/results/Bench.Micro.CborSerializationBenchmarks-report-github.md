```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method    | ItemCount | Mean        | Error     | StdDev    | Gen0   | Allocated |
|---------- |---------- |------------:|----------:|----------:|-------:|----------:|
| **Serialize** | **1**         |    **389.3 ns** |   **1.97 ns** |   **1.74 ns** | **0.0248** |     **208 B** |
| **Serialize** | **100**       | **28,531.0 ns** | **195.22 ns** | **182.61 ns** | **1.7395** |   **14832 B** |
