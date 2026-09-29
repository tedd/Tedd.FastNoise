```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26200.9457)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  net10  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=net10  IterationCount=12  LaunchCount=2
WarmupCount=4

```
| Method                    | Mean     | Error   | StdDev  | Ratio | RatioSD | Rank |
|-------------------------- |---------:|--------:|--------:|------:|--------:|-----:|
| &#39;Flip selected unit sign&#39; | 114.9 μs | 3.91 μs | 4.80 μs |  0.98 |    0.09 |    1 |
| &#39;Convert selected sign&#39;   | 117.5 μs | 7.73 μs | 9.49 μs |  1.01 |    0.11 |    1 |
