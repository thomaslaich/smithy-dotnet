```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method    | ItemCount | Mean        | Error    | StdDev   | Gen0   | Allocated |
|---------- |---------- |------------:|---------:|---------:|-------:|----------:|
| **Serialize** | **1**         |    **357.4 ns** |  **3.23 ns** |  **3.02 ns** | **0.0172** |     **144 B** |
| **Serialize** | **100**       | **25,945.8 ns** | **72.63 ns** | **64.39 ns** | **1.3733** |   **11600 B** |
