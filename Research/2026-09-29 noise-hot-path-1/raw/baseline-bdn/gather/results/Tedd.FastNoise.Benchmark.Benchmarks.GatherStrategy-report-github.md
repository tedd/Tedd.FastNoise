```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26200.9457)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  net10  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=net10  IterationCount=8  LaunchCount=2
WarmupCount=3

```
| Method                              | Mean     | Error    | StdDev   | Ratio | RatioSD | Rank |
|------------------------------------ |---------:|---------:|---------:|------:|--------:|-----:|
| &#39;VectorOps.Gather (as shipped)&#39;     | 356.9 μs |  1.86 μs |  1.83 μs |  1.00 |    0.01 |    1 |
| &#39;Software gather (spill and index)&#39; | 934.0 μs | 20.64 μs | 18.29 μs |  2.62 |    0.05 |    2 |
