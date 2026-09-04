using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tedd.FastNoise.Designer.Rendering;

/// <summary>The available colour mappings for a noise field.</summary>
public enum RampKind
{
    /// <summary>Black to white. The honest one: shows the field and nothing else.</summary>
    Greyscale = 0,

    /// <summary>Deep water through shallows, beach, grass, rock, snow. Reads as a map.</summary>
    Terrain = 1,

    /// <summary>Blue through white to red, centred on zero. Shows where a field crosses its threshold.</summary>
    Diverging = 2,

    /// <summary>Perceptually even blue-green-yellow. Best for judging gradients and banding.</summary>
    Viridis = 3,
}

/// <summary>
/// Maps a noise value in [-1, 1] to a colour, and exposes the mapping as a 1D texture.
/// </summary>
/// <remarks>
/// The 3D views texture their meshes with a 256x1 strip of this ramp, indexed by the sample value.
/// That keeps the 2D image, the heightmap and the voxel view visually consistent, and it means a
/// mesh needs one material rather than per-vertex colours, which WPF does not support anyway.
/// </remarks>
public static class ColourRamp
{
    /// <summary>Width of the generated texture strip.</summary>
    private const int TextureWidth = 256;

    /// <summary>Maps a value in [-1, 1] to a colour.</summary>
    /// <param name="kind">Which ramp to use.</param>
    /// <param name="value">The sample value. Values outside [-1, 1] are clamped.</param>
    public static (byte R, byte G, byte B) Map(RampKind kind, float value)
    {
        float unit = Math.Clamp((value + 1f) * 0.5f, 0f, 1f);

        return kind switch
        {
            RampKind.Terrain => Terrain(unit),
            RampKind.Diverging => Diverging(unit),
            RampKind.Viridis => Viridis(unit),
            _ => Greyscale(unit),
        };
    }

    /// <summary>Builds a 256x1 texture of the ramp, for use as a mesh material.</summary>
    /// <param name="kind">Which ramp to render.</param>
    public static ImageBrush BuildBrush(RampKind kind)
    {
        WriteableBitmap bitmap = new(TextureWidth, 1, 96, 96, PixelFormats.Bgra32, palette: null);
        byte[] pixels = new byte[TextureWidth * 4];

        for (int x = 0; x < TextureWidth; x++)
        {
            float value = (x / (TextureWidth - 1f) * 2f) - 1f;
            (byte r, byte g, byte b) = Map(kind, value);

            pixels[(x * 4) + 0] = b;
            pixels[(x * 4) + 1] = g;
            pixels[(x * 4) + 2] = r;
            pixels[(x * 4) + 3] = 255;
        }

        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, TextureWidth, 1), pixels, TextureWidth * 4, 0);
        bitmap.Freeze();

        ImageBrush brush = new(bitmap)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new System.Windows.Rect(0, 0, 1, 1),
            TileMode = TileMode.None,
            Stretch = Stretch.Fill,
        };

        brush.Freeze();
        return brush;
    }

    private static (byte, byte, byte) Greyscale(float unit)
    {
        byte level = (byte)(unit * 255f);
        return (level, level, level);
    }

    private static (byte, byte, byte) Terrain(float unit) => unit switch
    {
        < 0.36f => Blend((12, 28, 78), (28, 86, 160), unit / 0.36f),
        < 0.48f => Blend((28, 86, 160), (86, 160, 208), (unit - 0.36f) / 0.12f),
        < 0.52f => Blend((214, 205, 158), (196, 184, 126), (unit - 0.48f) / 0.04f),
        < 0.68f => Blend((92, 142, 74), (60, 108, 52), (unit - 0.52f) / 0.16f),
        < 0.84f => Blend((104, 100, 92), (140, 136, 128), (unit - 0.68f) / 0.16f),
        _ => Blend((190, 190, 190), (255, 255, 255), (unit - 0.84f) / 0.16f),
    };

    private static (byte, byte, byte) Diverging(float unit) => unit < 0.5f
        ? Blend((32, 82, 178), (246, 246, 246), unit / 0.5f)
        : Blend((246, 246, 246), (186, 42, 42), (unit - 0.5f) / 0.5f);

    /// <summary>A four-stop approximation of viridis. Close enough to keep its even lightness ramp.</summary>
    private static (byte, byte, byte) Viridis(float unit) => unit switch
    {
        < 0.33f => Blend((68, 1, 84), (59, 82, 139), unit / 0.33f),
        < 0.66f => Blend((59, 82, 139), (33, 145, 140), (unit - 0.33f) / 0.33f),
        < 0.85f => Blend((33, 145, 140), (94, 201, 98), (unit - 0.66f) / 0.19f),
        _ => Blend((94, 201, 98), (253, 231, 37), (unit - 0.85f) / 0.15f),
    };

    private static (byte, byte, byte) Blend((int R, int G, int B) from, (int R, int G, int B) to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return (
            (byte)(from.R + ((to.R - from.R) * t)),
            (byte)(from.G + ((to.G - from.G) * t)),
            (byte)(from.B + ((to.B - from.B) * t)));
    }
}
