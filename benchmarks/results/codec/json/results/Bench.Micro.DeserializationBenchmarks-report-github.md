```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                    | Scenario           | Mean         | Error      | StdDev     | Ratio | Gen0    | Gen1    | Allocated | Alloc Ratio |
|-------------------------- |------------------- |-------------:|-----------:|-----------:|------:|--------:|--------:|----------:|------------:|
| **&#39;STJ source-gen&#39;**          | **create-order-large** | **3,384.813 μs** | **24.5173 μs** | **22.9335 μs** |  **1.00** | **97.6563** | **46.8750** | **4741304 B** |       **1.000** |
| &#39;NSmithy schema codec&#39;    | create-order-large | 3,529.517 μs | 36.2535 μs | 33.9115 μs |  1.04 | 97.6563 | 46.8750 | 4433660 B |       0.935 |
| &#39;JsonDocument.Parse only&#39; | create-order-large | 1,464.645 μs |  6.8070 μs |  6.0342 μs |  0.43 |       - |       - |      83 B |       0.000 |
|                           |                    |              |            |            |       |         |         |           |             |
| **&#39;STJ source-gen&#39;**          | **create-order-small** |     **3.736 μs** |  **0.0275 μs** |  **0.0258 μs** |  **1.00** |  **0.6866** |  **0.0076** |    **5760 B** |        **1.00** |
| &#39;NSmithy schema codec&#39;    | create-order-small |     4.160 μs |  0.0190 μs |  0.0177 μs |  1.11 |  0.6027 |  0.0076 |    5056 B |        0.88 |
| &#39;JsonDocument.Parse only&#39; | create-order-small |     1.767 μs |  0.0053 μs |  0.0049 μs |  0.47 |  0.0076 |       - |      72 B |        0.01 |
