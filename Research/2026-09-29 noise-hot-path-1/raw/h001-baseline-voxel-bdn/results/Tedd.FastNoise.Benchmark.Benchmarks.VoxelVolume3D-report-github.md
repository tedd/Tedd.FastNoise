```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26200.9457)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  net10  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=net10  IterationCount=10  LaunchCount=2
WarmupCount=4

```
| Method                | Type         | Mean      | Error     | StdDev    | Rank | Allocated |
|---------------------- |------------- |----------:|----------:|----------:|-----:|----------:|
| &#39;Tedd.FastNoise SIMD&#39; | Value        |  2.840 ms | 0.1432 ms | 0.1649 ms |    1 |     176 B |
| &#39;Tedd.FastNoise SIMD&#39; | OpenSimplex2 |  5.121 ms | 0.0587 ms | 0.0676 ms |    2 |     243 B |
| &#39;Tedd.FastNoise SIMD&#39; | Perlin       |  5.509 ms | 0.0226 ms | 0.0232 ms |    3 |     243 B |
| &#39;Tedd.FastNoise SIMD&#39; | Cellular     | 14.947 ms | 0.5429 ms | 0.5809 ms |    4 |     176 B |
