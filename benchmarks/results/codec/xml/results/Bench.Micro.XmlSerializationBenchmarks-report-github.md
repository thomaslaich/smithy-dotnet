```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method    | ItemCount | Mean      | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|---------- |---------- |----------:|----------:|----------:|--------:|-------:|----------:|
| **Serialize** | **1**         |  **1.507 μs** | **0.0080 μs** | **0.0067 μs** |  **1.9226** | **0.0954** |  **15.72 KB** |
| **Serialize** | **100**       | **69.074 μs** | **0.3264 μs** | **0.2893 μs** | **22.7051** | **5.6152** | **185.99 KB** |
