```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain

```
| Method    | ItemCount | Mean       | Error     | StdDev    | Gen0    | Gen1   | Allocated |
|---------- |---------- |-----------:|----------:|----------:|--------:|-------:|----------:|
| **Serialize** | **1**         |   **1.954 μs** | **0.0063 μs** | **0.0059 μs** |  **1.9302** | **0.0458** |  **15.78 KB** |
| **Serialize** | **100**       | **101.942 μs** | **1.0360 μs** | **0.9184 μs** | **23.0713** | **4.2725** | **189.15 KB** |
