```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method    | ItemCount | Mean        | Error    | StdDev   | Gen0   | Allocated |
|---------- |---------- |------------:|---------:|---------:|-------:|----------:|
| **Serialize** | **1**         |    **342.9 ns** |  **1.64 ns** |  **1.53 ns** | **0.0172** |     **144 B** |
| **Serialize** | **100**       | **25,219.3 ns** | **95.72 ns** | **84.85 ns** | **1.3733** |   **11600 B** |
