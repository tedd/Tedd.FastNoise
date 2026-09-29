using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Tedd.FastNoise;

NoiseType type = Enum.Parse<NoiseType>(args[0]);
string label = args[1];

Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)0x10;

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

for (int index = 0; index < 100; index++)
{
    noise.Fill(destination, region, NoiseBackend.Simd);
}

// Tiered PGO compiles in the background. Give the compilation queue a quiescent window, then
// execute enough additional calls to ensure the optimized entry point is active before timing.
Thread.Sleep(250);
for (int index = 0; index < 20; index++)
{
    noise.Fill(destination, region, NoiseBackend.Simd);
}

for (int sample = 0; sample < 30; sample++)
{
    long start = Stopwatch.GetTimestamp();
    noise.Fill(destination, region, NoiseBackend.Simd);
    long stop = Stopwatch.GetTimestamp();
    double microseconds = Stopwatch.GetElapsedTime(start, stop).TotalMicroseconds;
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"{label},{type},{sample},{microseconds:F3},{destination[0]:R},{destination[^1]:R}"));
}
