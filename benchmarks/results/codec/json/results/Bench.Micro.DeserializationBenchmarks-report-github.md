```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.201
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                    | Scenario           | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0     | Gen1    | Allocated | Alloc Ratio |
|-------------------------- |------------------- |-------------:|-----------:|-----------:|------:|--------:|---------:|--------:|----------:|------------:|
| **&#39;STJ source-gen&#39;**          | **create-order-large** | **3,320.239 μs** | **39.7283 μs** | **37.1618 μs** |  **1.00** |    **0.02** | **101.5625** | **46.8750** | **4741304 B** |       **1.000** |
| &#39;NSmithy schema codec&#39;    | create-order-large | 2,608.996 μs | 26.8700 μs | 25.1342 μs |  0.79 |    0.01 |  93.7500 | 42.9688 | 4279920 B |       0.903 |
| &#39;JsonDocument.Parse only&#39; | create-order-large | 1,417.866 μs | 16.3721 μs | 15.3144 μs |  0.43 |    0.01 |        - |       - |      82 B |       0.000 |
|                           |                    |              |            |            |       |         |          |         |           |             |
| **&#39;STJ source-gen&#39;**          | **create-order-small** |     **3.659 μs** |  **0.0195 μs** |  **0.0182 μs** |  **1.00** |    **0.01** |   **0.6866** |  **0.0076** |    **5760 B** |        **1.00** |
| &#39;NSmithy schema codec&#39;    | create-order-small |     2.817 μs |  0.0186 μs |  0.0165 μs |  0.77 |    0.01 |   0.5722 |  0.0076 |    4816 B |        0.84 |
| &#39;JsonDocument.Parse only&#39; | create-order-small |     1.723 μs |  0.0132 μs |  0.0124 μs |  0.47 |    0.00 |   0.0076 |       - |      72 B |        0.01 |
