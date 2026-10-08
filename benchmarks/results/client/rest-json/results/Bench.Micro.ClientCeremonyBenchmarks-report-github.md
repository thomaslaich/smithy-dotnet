```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method | Client       | Mean       | Error    | StdDev   | Gen0   | Gen1   | Allocated |
|------- |------------- |-----------:|---------:|---------:|-------:|-------:|----------:|
| **Call**   | **hand-written** |   **996.4 ns** | **10.60 ns** |  **9.91 ns** | **0.3242** |      **-** |   **2.66 KB** |
| **Call**   | **nsmithy**      | **1,577.4 ns** | **31.37 ns** | **30.81 ns** | **0.5798** | **0.0019** |   **4.75 KB** |
