using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Tedd.FastNoise;

if (args.Length != 4)
    throw new ArgumentException("Expected revision, raw CSV path, site JSON path, and site JavaScript path.");

const int warmups = 8;
const int observations = 13;
const int samples = 65_536;

NoiseType[] types =
[
    NoiseType.OpenSimplex2, NoiseType.OpenSimplex2S, NoiseType.Perlin,
    NoiseType.Value, NoiseType.ValueCubic, NoiseType.Cellular,
];
string[] shapes = ["2D 256×256", "3D 16×256×16"];
string[] fractals = ["Single", "fBm ×4"];
string[] accessModes = ["Point loop", "Scalar fill", "SIMD fill", "Parallel fill"];

var results = new List<object>();
var raw = new StringBuilder("shape,fractal,algorithm,access,observation,milliseconds,checksum\n");

foreach (string shape in shapes)
foreach (string fractal in fractals)
foreach (NoiseType type in types)
{
    var generator = new NoiseGenerator(1337)
    {
        NoiseType = type,
        FractalType = fractal == "Single" ? FractalType.None : FractalType.FBm,
        Octaves = fractal == "Single" ? 1 : 4,
        Frequency = 0.02f,
        ParallelThreshold = 1,
    };
    var destination = new float[samples];
    GridRegion2D region2 = new(0f, 0f, 256, 256);
    GridRegion3D region3 = new(0f, 0f, 0f, 16, 256, 16);
    ulong? referenceChecksum = null;

    foreach (string access in accessModes)
    {
        void Evaluate()
        {
            if (access == "Point loop")
            {
                int index = 0;
                if (shape.StartsWith("2D", StringComparison.Ordinal))
                {
                    for (int y = 0; y < 256; y++)
                    for (int x = 0; x < 256; x++)
                        destination[index++] = generator.GetNoise(x, y);
                }
                else
                {
                    for (int z = 0; z < 16; z++)
                    for (int y = 0; y < 256; y++)
                    for (int x = 0; x < 16; x++)
                        destination[index++] = generator.GetNoise(x, y, z);
                }
                return;
            }

            NoiseBackend backend = access switch
            {
                "Scalar fill" => NoiseBackend.Scalar,
                "SIMD fill" => NoiseBackend.Simd,
                "Parallel fill" => NoiseBackend.Parallel,
                _ => throw new InvalidOperationException(access),
            };
            if (shape.StartsWith("2D", StringComparison.Ordinal))
                generator.Fill(destination, region2, backend);
            else
                generator.Fill(destination, region3, backend);
        }

        for (int i = 0; i < warmups; i++) Evaluate();
        var times = new double[observations];
        for (int i = 0; i < observations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            Evaluate();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            ulong checksum = Checksum(destination);
            raw.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{Csv(shape)},{Csv(fractal)},{type},{Csv(access)},{i},{times[i]:F6},{checksum:X16}"));
        }

        ulong finalChecksum = Checksum(destination);
        if (referenceChecksum is null) referenceChecksum = finalChecksum;
        else if (finalChecksum != referenceChecksum)
            throw new InvalidOperationException($"Output mismatch: {shape}, {fractal}, {type}, {access}.");

        Array.Sort(times);
        double median = times[observations / 2];
        double p10 = Percentile(times, .1);
        double p90 = Percentile(times, .9);
        results.Add(new
        {
            shape, fractal, algorithm = type.ToString(), access,
            medianMs = Math.Round(median, 4),
            p10Ms = Math.Round(p10, 4),
            p90Ms = Math.Round(p90, 4),
            minMs = Math.Round(times[0], 4),
            maxMs = Math.Round(times[^1], 4),
            checksum = finalChecksum.ToString("X16", CultureInfo.InvariantCulture),
        });
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{shape} | {fractal} | {type} | {access}: {median:F4} ms"));
    }
}

File.WriteAllText(args[1], raw.ToString(), new UTF8Encoding(false));
var summary = new
{
    generatedAt = DateTimeOffset.Now,
    revision = args[0],
    runtime = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    logicalProcessors = Environment.ProcessorCount,
    operatingSystem = RuntimeInformation.OSDescription,
    cpu = Environment.GetEnvironmentVariable("FASTNOISE_BENCH_CPU")
        ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
        ?? "Unreported",
    sampleCount = samples,
    warmups,
    observations,
    method = "One process, Release build, ReadyToRun=0, tiered compilation=0; warmed each case, then timed complete calls. Median and 10th–90th percentile of 13 observations. Destination preallocated; checksum computed outside the timed interval.",
    results,
};
string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[2], json + "\n", new UTF8Encoding(false));
File.WriteAllText(args[3], "window.fastNoiseBenchmarks = " + json + ";\n", new UTF8Encoding(false));

static ulong Checksum(float[] data)
{
    ulong hash = 14695981039346656037UL;
    foreach (float value in data)
    {
        hash ^= (uint)BitConverter.SingleToInt32Bits(value);
        hash *= 1099511628211UL;
    }
    return hash;
}

static double Percentile(double[] sorted, double fraction)
{
    double position = (sorted.Length - 1) * fraction;
    int lower = (int)position;
    int upper = Math.Min(lower + 1, sorted.Length - 1);
    return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
}

static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
