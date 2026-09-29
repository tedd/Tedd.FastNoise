using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Tedd.FastNoise;

if (args.Length != 4)
{
    throw new ArgumentException("Expected label, noise type, mode (point|simd|parallel), and size (standard|large).");
}

string label = args[0];
NoiseType type = Enum.Parse<NoiseType>(args[1]);
string mode = args[2];
string size = args[3];
if (mode is not ("point" or "simd" or "parallel"))
{
    throw new ArgumentException("Unknown mode.");
}

Process.GetCurrentProcess().ProcessorAffinity = mode == "parallel"
    ? (IntPtr)0x5555_5555
    : (IntPtr)0x10;

GridRegion3D region = size switch
{
    "standard" => new(0f, 0f, 0f, 16, 256, 16),
    "large" => new(0f, 0f, 0f, 32, 256, 32),
    _ => throw new ArgumentException("Unknown size."),
};
float[] destination = new float[region.SampleCount];
NoiseGenerator noise = new(1337)
{
    NoiseType = type,
    FractalType = FractalType.FBm,
    Octaves = 4,
    Frequency = 0.02f,
    ParallelThreshold = 1,
};

double Evaluate()
{
    if (mode == "point")
    {
        double sum = 0;
        int output = 0;
        for (int z = 0; z < region.Depth; z++)
        {
            for (int y = 0; y < region.Height; y++)
            {
                for (int x = 0; x < region.Width; x++)
                {
                    float sample = noise.GetNoise(x, y, z);
                    destination[output++] = sample;
                    sum += sample;
                }
            }
        }

        return sum;
    }

    noise.Fill(destination, region, mode == "parallel" ? NoiseBackend.Parallel : NoiseBackend.Simd);
    double checksum = 0;
    foreach (float sample in destination)
    {
        checksum += sample;
    }

    return checksum;
}

for (int warmup = 0; warmup < 20; warmup++)
{
    _ = Evaluate();
}

Thread.Sleep(250);
for (int warmup = 0; warmup < 10; warmup++)
{
    _ = Evaluate();
}

for (int observation = 0; observation < 20; observation++)
{
    long start = Stopwatch.GetTimestamp();
    double checksum = Evaluate();
    long end = Stopwatch.GetTimestamp();
    double microseconds = Stopwatch.GetElapsedTime(start, end).TotalMicroseconds;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{label},{type},{mode},{observation},{microseconds:F3},{checksum:R},{destination[0]:R},{destination[^1]:R}"));
}
