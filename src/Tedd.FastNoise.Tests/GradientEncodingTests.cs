using System;
using Tedd.FastNoise.Internal;

namespace Tedd.FastNoise.Tests;

public class GradientEncodingTests
{
    [Fact]
    public void Packed3DGradientCodes_ReconstructTheReferenceTableExactly()
    {
        ReadOnlySpan<int> codes = Tables.GradientCodes3D;
        ReadOnlySpan<float> gradients = Tables.Gradients3D;

        Assert.Equal(64, codes.Length);
        Assert.Equal(codes.Length * 4, gradients.Length);

        for (int index = 0; index < codes.Length; index++)
        {
            int code = codes[index];
            int zeroAxis = code & 3;

            Assert.Equal(gradients[(index * 4) + 0], Component(code, zeroAxis, axis: 0));
            Assert.Equal(gradients[(index * 4) + 1], Component(code, zeroAxis, axis: 1));
            Assert.Equal(gradients[(index * 4) + 2], Component(code, zeroAxis, axis: 2));
            Assert.Equal(0f, gradients[(index * 4) + 3]);
        }
    }

    private static float Component(int code, int zeroAxis, int axis)
    {
        if (axis == zeroAxis)
        {
            return 0f;
        }

        return (code & (1 << (axis + 2))) == 0 ? 1f : -1f;
    }
}
