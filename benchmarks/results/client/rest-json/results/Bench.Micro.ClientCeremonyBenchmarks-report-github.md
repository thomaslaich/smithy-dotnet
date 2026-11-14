```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method | Client       | Mean       | Error    | StdDev   | Gen0   | Gen1   | Allocated |
|------- |------------- |-----------:|---------:|---------:|-------:|-------:|----------:|
| **Call**   | **hand-written** |   **917.5 ns** |  **4.10 ns** |  **3.64 ns** | **0.3242** |      **-** |   **2.66 KB** |
| **Call**   | **nsmithy**      | **1,384.9 ns** | **12.09 ns** | **11.30 ns** | **0.5722** | **0.0019** |   **4.69 KB** |
