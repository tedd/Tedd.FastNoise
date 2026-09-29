using System;
using System.Diagnostics;
using Tedd.FastNoise;

NoiseType type = args.Length == 0 ? NoiseType.OpenSimplex2 : Enum.Parse<NoiseType>(args[0]);
TimeSpan duration = TimeSpan.FromSeconds(args.Length < 2 ? 12 : int.Parse(args[1]));

GridRegion3D region = new(0f, 0f, 0f, 16, 256, 16);
float[] destination = new float[region.SampleCount];
NoiseGenerator noise = new(1337)
{
    NoiseType = type,
    FractalType = FractalType.FBm,
    Octaves = 4,
    Frequency = 0.02f,
    ParallelThreshold = int.MaxValue,
};

for (int index = 0; index < 20; index++)
{
    noise.Fill(destination, region, NoiseBackend.Simd);
}

long fills = 0;
Stopwatch stopwatch = Stopwatch.StartNew();
while (stopwatch.Elapsed < duration)
{
    noise.Fill(destination, region, NoiseBackend.Simd);
    fills++;
}

Console.WriteLine($"type={type}; fills={fills}; elapsed={stopwatch.Elapsed.TotalSeconds:F3}; checksum={destination[0]:R},{destination[^1]:R}");
