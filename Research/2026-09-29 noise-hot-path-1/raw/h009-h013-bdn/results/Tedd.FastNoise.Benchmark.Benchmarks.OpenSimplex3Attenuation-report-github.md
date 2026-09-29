```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26200.9457)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  net10  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=net10  IterationCount=12  LaunchCount=2
WarmupCount=4

```
| Method                 | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Rank |
|----------------------- |---------:|---------:|---------:|---------:|------:|--------:|-----:|
| &#39;Compare and select&#39;   | 116.1 μs | 11.64 μs | 14.72 μs | 103.1 μs |  1.02 |    0.18 |    1 |
| &#39;Clamp before quartic&#39; | 123.8 μs |  1.07 μs |  1.35 μs | 123.2 μs |  1.08 |    0.13 |    1 |
