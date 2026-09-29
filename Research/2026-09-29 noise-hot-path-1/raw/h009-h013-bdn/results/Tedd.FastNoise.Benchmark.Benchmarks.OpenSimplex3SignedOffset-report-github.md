```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26200.9457)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  net10  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=net10  IterationCount=12  LaunchCount=2
WarmupCount=4

```
| Method                      | Mean     | Error   | StdDev  | Ratio | RatioSD | Rank |
|---------------------------- |---------:|--------:|--------:|------:|--------:|-----:|
| &#39;Flip sign bit&#39;             | 112.4 μs | 4.83 μs | 6.11 μs |  0.99 |    0.07 |    1 |
| &#39;Convert sign and multiply&#39; | 113.5 μs | 3.68 μs | 4.52 μs |  1.00 |    0.06 |    1 |
