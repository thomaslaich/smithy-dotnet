```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                    | Scenario           | Mean         | Error      | StdDev     | Ratio | Gen0    | Gen1    | Allocated | Alloc Ratio |
|-------------------------- |------------------- |-------------:|-----------:|-----------:|------:|--------:|--------:|----------:|------------:|
| **&#39;STJ source-gen&#39;**          | **create-order-large** | **3,408.099 μs** | **37.6276 μs** | **35.1969 μs** |  **1.00** | **97.6563** | **46.8750** | **4741304 B** |       **1.000** |
| &#39;NSmithy schema codec&#39;    | create-order-large | 3,504.663 μs | 21.4746 μs | 19.0367 μs |  1.03 | 89.8438 | 42.9688 | 4433648 B |       0.935 |
| &#39;JsonDocument.Parse only&#39; | create-order-large | 1,489.305 μs | 10.8285 μs | 10.1290 μs |  0.44 |       - |       - |      81 B |       0.000 |
|                           |                    |              |            |            |       |         |         |           |             |
| **&#39;STJ source-gen&#39;**          | **create-order-small** |     **3.791 μs** |  **0.0121 μs** |  **0.0113 μs** |  **1.00** |  **0.6866** |  **0.0076** |    **5760 B** |        **1.00** |
| &#39;NSmithy schema codec&#39;    | create-order-small |     4.134 μs |  0.0137 μs |  0.0128 μs |  1.09 |  0.6027 |  0.0076 |    5056 B |        0.88 |
| &#39;JsonDocument.Parse only&#39; | create-order-small |     1.749 μs |  0.0126 μs |  0.0112 μs |  0.46 |  0.0076 |       - |      72 B |        0.01 |
