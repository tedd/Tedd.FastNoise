using Tedd.FastNoise;

NoiseGenerator noise = new(1337)
{
    NoiseType = NoiseType.OpenSimplex2,
    FractalType = FractalType.FBm,
    Octaves = 4,
    Frequency = 0.02f,
};

float[] values =
[
    0f, -0f, 0.5f, -0.5f, 1f, -1f, 100f, -100f,
    float.Epsilon, -float.Epsilon, float.MaxValue, float.MinValue,
    float.NaN, float.PositiveInfinity, float.NegativeInfinity,
];

for (int axis = 0; axis < 3; axis++)
{
    foreach (float value in values)
    {
        float x = axis == 0 ? value : 0.25f;
        float y = axis == 1 ? value : 0.125f;
        float z = axis == 2 ? value : -0.375f;
        float sample = noise.GetNoise(x, y, z);
        Console.WriteLine($"{axis},{BitConverter.SingleToInt32Bits(value):X8},{BitConverter.SingleToInt32Bits(sample):X8}");
    }
}

GridRegion3D region = new(-0f, -0f, -0f, 9, 3, 2);
float[] output = new float[region.SampleCount];
foreach (NoiseBackend backend in new[] { NoiseBackend.Scalar, NoiseBackend.Simd, NoiseBackend.Parallel })
{
    noise.ParallelThreshold = 1;
    noise.Fill(output, region, backend);
    foreach (float value in output)
    {
        Console.WriteLine($"bulk-{backend},{BitConverter.SingleToInt32Bits(value):X8}");
    }
}
