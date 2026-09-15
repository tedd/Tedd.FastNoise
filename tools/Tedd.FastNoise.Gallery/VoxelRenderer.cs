using System;

namespace Tedd.FastNoise.Gallery;

/// <summary>Projects a thresholded 3D field into an isometric voxel image.</summary>
internal static class VoxelRenderer
{
    private const int Width = 960;
    private const int Height = 880;
    private const int Scale = 8;
    private static readonly (byte R, byte G, byte B) Background = (13, 34, 39);

    public static (int Width, int Height, byte[] Pixels) Render(float[] field, int edge, float threshold)
    {
        bool[] solid = new bool[field.Length];
        for (int index = 0; index < field.Length; index++)
        {
            solid[index] = field[index] >= threshold;
        }

        byte[] pixels = new byte[Width * Height * 3];
        Clear(pixels, Background);

        int originX = Width / 2;
        int originY = 96 + (edge * Scale);

        // Painter's order: low x+z is farthest from a camera on the +X,+Z diagonal.
        for (int depth = 0; depth <= (edge - 1) * 2; depth++)
        {
            for (int y = 0; y < edge; y++)
            {
                int firstX = Math.Max(0, depth - (edge - 1));
                int lastX = Math.Min(edge - 1, depth);

                for (int x = firstX; x <= lastX; x++)
                {
                    int z = depth - x;
                    int index = Index(x, y, z, edge);
                    if (!solid[index])
                    {
                        continue;
                    }

                    float unit = Math.Clamp((field[index] + 1f) * 0.5f, 0f, 1f);
                    (byte r, byte g, byte b) = SurfaceColour(unit);

                    if (x == edge - 1 || !solid[Index(x + 1, y, z, edge)])
                    {
                        FillQuad(pixels,
                            Project(x + 1, y, z, originX, originY),
                            Project(x + 1, y, z + 1, originX, originY),
                            Project(x + 1, y + 1, z + 1, originX, originY),
                            Project(x + 1, y + 1, z, originX, originY),
                            Shade((r, g, b), 0.58f));
                    }

                    if (z == edge - 1 || !solid[Index(x, y, z + 1, edge)])
                    {
                        FillQuad(pixels,
                            Project(x, y, z + 1, originX, originY),
                            Project(x, y + 1, z + 1, originX, originY),
                            Project(x + 1, y + 1, z + 1, originX, originY),
                            Project(x + 1, y, z + 1, originX, originY),
                            Shade((r, g, b), 0.76f));
                    }

                    if (y == edge - 1 || !solid[Index(x, y + 1, z, edge)])
                    {
                        FillQuad(pixels,
                            Project(x, y + 1, z, originX, originY),
                            Project(x + 1, y + 1, z, originX, originY),
                            Project(x + 1, y + 1, z + 1, originX, originY),
                            Project(x, y + 1, z + 1, originX, originY),
                            Shade((r, g, b), 1.12f));
                    }
                }
            }
        }

        return (Width, Height, pixels);
    }

    private static int Index(int x, int y, int z, int edge) => x + (edge * (y + (edge * z)));

    private static Point Project(int x, int y, int z, int originX, int originY)
        => new(
            originX + ((x - z) * Scale),
            originY + (((x + z) * Scale) / 2) - (y * Scale));

    private static void FillQuad(byte[] pixels, Point a, Point b, Point c, Point d, (byte R, byte G, byte B) colour)
    {
        FillTriangle(pixels, a, b, c, colour);
        FillTriangle(pixels, a, c, d, colour);
    }

    private static void FillTriangle(byte[] pixels, Point a, Point b, Point c, (byte R, byte G, byte B) colour)
    {
        int minX = Math.Clamp(Math.Min(a.X, Math.Min(b.X, c.X)), 0, Width - 1);
        int maxX = Math.Clamp(Math.Max(a.X, Math.Max(b.X, c.X)), 0, Width - 1);
        int minY = Math.Clamp(Math.Min(a.Y, Math.Min(b.Y, c.Y)), 0, Height - 1);
        int maxY = Math.Clamp(Math.Max(a.Y, Math.Max(b.Y, c.Y)), 0, Height - 1);
        int orientation = Edge(a, b, c);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Point point = new(x, y);
                int ab = Edge(a, b, point);
                int bc = Edge(b, c, point);
                int ca = Edge(c, a, point);

                bool inside = orientation >= 0
                    ? ab >= 0 && bc >= 0 && ca >= 0
                    : ab <= 0 && bc <= 0 && ca <= 0;

                if (inside)
                {
                    SetPixel(pixels, x, y, colour);
                }
            }
        }
    }

    private static int Edge(Point a, Point b, Point point)
        => ((point.X - a.X) * (b.Y - a.Y)) - ((point.Y - a.Y) * (b.X - a.X));

    private static (byte, byte, byte) SurfaceColour(float unit)
    {
        float t = Math.Clamp((unit - 0.48f) / 0.52f, 0f, 1f);
        return (
            (byte)(20 + (80 * t)),
            (byte)(122 + (105 * t)),
            (byte)(115 + (90 * t)));
    }

    private static (byte, byte, byte) Shade((byte R, byte G, byte B) colour, float factor)
        => (
            (byte)Math.Clamp(colour.R * factor, 0f, 255f),
            (byte)Math.Clamp(colour.G * factor, 0f, 255f),
            (byte)Math.Clamp(colour.B * factor, 0f, 255f));

    private static void Clear(byte[] pixels, (byte R, byte G, byte B) colour)
    {
        for (int index = 0; index < pixels.Length; index += 3)
        {
            pixels[index] = colour.R;
            pixels[index + 1] = colour.G;
            pixels[index + 2] = colour.B;
        }
    }

    private static void SetPixel(byte[] pixels, int x, int y, (byte R, byte G, byte B) colour)
    {
        int index = ((y * Width) + x) * 3;
        pixels[index] = colour.R;
        pixels[index + 1] = colour.G;
        pixels[index + 2] = colour.B;
    }

    private readonly record struct Point(int X, int Y);
}
