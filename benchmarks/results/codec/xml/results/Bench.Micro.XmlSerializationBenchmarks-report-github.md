```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method    | ItemCount | Mean      | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|---------- |---------- |----------:|----------:|----------:|--------:|-------:|----------:|
| **Serialize** | **1**         |  **1.461 μs** | **0.0148 μs** | **0.0139 μs** |  **1.9226** | **0.0954** |  **15.72 KB** |
| **Serialize** | **100**       | **69.282 μs** | **0.2473 μs** | **0.2065 μs** | **22.7051** | **5.6152** | **185.99 KB** |
