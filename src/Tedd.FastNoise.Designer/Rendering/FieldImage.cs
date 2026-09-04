using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tedd.FastNoise.Designer.Rendering;

/// <summary>Turns a filled noise field into a bitmap for the 2D preview.</summary>
public static class FieldImage
{
    /// <summary>Renders a 2D field as a bitmap.</summary>
    /// <param name="field">Sample values, X-fastest.</param>
    /// <param name="width">Samples along X.</param>
    /// <param name="height">Samples along Y.</param>
    /// <param name="ramp">Colour mapping.</param>
    /// <param name="threshold">
    /// When <paramref name="showThreshold"/> is set, values at or above this are drawn solid and
    /// values below are drawn as empty space.
    /// </param>
    /// <param name="showThreshold">Whether to draw the field as a solid/empty mask instead of a gradient.</param>
    /// <returns>A frozen bitmap, safe to hand to the UI thread from a worker.</returns>
    public static BitmapSource Render(
        ReadOnlySpan<float> field,
        int width,
        int height,
        RampKind ramp,
        float threshold,
        bool showThreshold)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int index = 0; index < width * height; index++)
        {
            float value = field[index];
            byte r, g, b;

            if (showThreshold)
            {
                bool solid = value >= threshold;
                (r, g, b) = solid ? ColourRamp.Map(ramp, value) : ((byte)24, (byte)25, (byte)28);
            }
            else
            {
                (r, g, b) = ColourRamp.Map(ramp, value);
            }

            pixels[(index * 4) + 0] = b;
            pixels[(index * 4) + 1] = g;
            pixels[(index * 4) + 2] = r;
            pixels[(index * 4) + 3] = 255;
        }

        WriteableBitmap bitmap = new(width, height, 96, 96, PixelFormats.Bgra32, palette: null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();

        return bitmap;
    }
}
