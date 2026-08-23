```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method | Client       | Mean       | Error    | StdDev   | Gen0   | Gen1   | Allocated |
|------- |------------- |-----------:|---------:|---------:|-------:|-------:|----------:|
| **Call**   | **hand-written** |   **956.7 ns** | **11.53 ns** | **10.22 ns** | **0.3242** |      **-** |   **2.66 KB** |
| **Call**   | **nsmithy**      | **1,788.4 ns** | **17.12 ns** | **16.01 ns** | **0.5989** | **0.0019** |    **4.9 KB** |
