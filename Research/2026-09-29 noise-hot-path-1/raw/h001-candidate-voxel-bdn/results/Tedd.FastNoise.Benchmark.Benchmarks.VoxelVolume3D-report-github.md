```

BenchmarkDotNet v0.15.4, Windows 11 (10.0.26200.9457)
AMD Ryzen 9 5950X 3.40GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  net10  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=net10  IterationCount=10  LaunchCount=2
WarmupCount=4

```
| Method                | Type         | Mean      | Error      | StdDev     | Rank | Allocated |
|---------------------- |------------- |----------:|-----------:|-----------:|-----:|----------:|
| &#39;Tedd.FastNoise SIMD&#39; | Value        |  2.830 ms |  0.2779 ms |  0.2974 ms |    1 |     176 B |
| &#39;Tedd.FastNoise SIMD&#39; | Perlin       |  4.554 ms |  0.0066 ms |  0.0076 ms |    2 |     243 B |
| &#39;Tedd.FastNoise SIMD&#39; | OpenSimplex2 |  5.198 ms |  0.3605 ms |  0.4151 ms |    3 |     260 B |
| &#39;Tedd.FastNoise SIMD&#39; | Cellular     | 85.137 ms | 62.6080 ms | 72.0995 ms |    4 |     512 B |
