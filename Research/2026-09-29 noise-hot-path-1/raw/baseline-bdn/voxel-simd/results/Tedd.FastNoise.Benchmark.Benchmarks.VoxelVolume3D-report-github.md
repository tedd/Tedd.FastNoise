```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26200.9457)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  net10  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=net10  IterationCount=8  LaunchCount=2
WarmupCount=3

```
| Method                | Type         | Mean      | Error     | StdDev    | Median    | Rank | Allocated |
|---------------------- |------------- |----------:|----------:|----------:|----------:|-----:|----------:|
| &#39;Tedd.FastNoise SIMD&#39; | Value        |  5.107 ms | 2.2384 ms | 2.0938 ms |  3.405 ms |    1 |     176 B |
| &#39;Tedd.FastNoise SIMD&#39; | OpenSimplex2 |  5.450 ms | 0.4825 ms | 0.4514 ms |  5.502 ms |    2 |     176 B |
| &#39;Tedd.FastNoise SIMD&#39; | Perlin       |  6.394 ms | 0.2527 ms | 0.2363 ms |  6.284 ms |    3 |     176 B |
| &#39;Tedd.FastNoise SIMD&#39; | Cellular     | 20.080 ms | 1.8680 ms | 1.8346 ms | 20.501 ms |    4 |     176 B |
