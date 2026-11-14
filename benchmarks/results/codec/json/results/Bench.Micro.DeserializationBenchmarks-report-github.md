```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), Arm64 RyuJIT armv8.0-a

Toolchain=InProcessEmitToolchain  

```
| Method                    | Scenario           | Mean         | Error      | StdDev     | Ratio | Gen0     | Gen1    | Allocated | Alloc Ratio |
|-------------------------- |------------------- |-------------:|-----------:|-----------:|------:|---------:|--------:|----------:|------------:|
| **&#39;STJ source-gen&#39;**          | **create-order-large** | **3,377.331 μs** | **24.9971 μs** | **22.1593 μs** |  **1.00** | **121.0938** | **58.5938** | **4741304 B** |       **1.000** |
| &#39;NSmithy schema codec&#39;    | create-order-large | 2,635.880 μs | 33.0671 μs | 30.9310 μs |  0.78 |  97.6563 | 46.8750 | 4331166 B |       0.913 |
| &#39;JsonDocument.Parse only&#39; | create-order-large | 1,462.142 μs | 14.6048 μs | 13.6613 μs |  0.43 |        - |       - |      83 B |       0.000 |
|                           |                    |              |            |            |       |          |         |           |             |
| **&#39;STJ source-gen&#39;**          | **create-order-small** |     **3.758 μs** |  **0.0132 μs** |  **0.0124 μs** |  **1.00** |   **0.6866** |  **0.0076** |    **5760 B** |        **1.00** |
| &#39;NSmithy schema codec&#39;    | create-order-small |     2.833 μs |  0.0219 μs |  0.0205 μs |  0.75 |   0.5798 |  0.0076 |    4880 B |        0.85 |
| &#39;JsonDocument.Parse only&#39; | create-order-small |     1.753 μs |  0.0107 μs |  0.0095 μs |  0.47 |   0.0076 |       - |      72 B |        0.01 |
