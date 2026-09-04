using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Tedd.FastNoise.Designer.Rendering;

/// <summary>Turns a 2D field into a displaced grid mesh: the same map, seen as terrain.</summary>
/// <remarks>
/// Vertices carry a texture coordinate derived from the sample value, so the mesh takes its colour
/// from the same ramp as the 2D view through a single-material 1D texture. WPF has no per-vertex
/// colour, and this is the cheap way round that.
/// </remarks>
public static class HeightmapMesh
{
    /// <summary>Builds the mesh.</summary>
    /// <param name="field">Sample values, X-fastest.</param>
    /// <param name="width">Samples along X.</param>
    /// <param name="height">Samples along Y.</param>
    /// <param name="heightScale">Vertical exaggeration. 0 gives a flat plane.</param>
    /// <returns>A frozen mesh spanning roughly [-0.5, 0.5] in X and Z.</returns>
    public static MeshGeometry3D Build(ReadOnlySpan<float> field, int width, int height, float heightScale)
    {
        Point3DCollection positions = new(width * height);
        Vector3DCollection normals = new(width * height);
        PointCollection textureCoordinates = new(width * height);
        Int32Collection indices = new((width - 1) * (height - 1) * 6);

        float stepX = 1f / MathF.Max(1f, width - 1);
        float stepY = 1f / MathF.Max(1f, height - 1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = field[x + (y * width)];

                positions.Add(new Point3D(
                    (x * stepX) - 0.5,
                    value * heightScale,
                    (y * stepY) - 0.5));

                normals.Add(EstimateNormal(field, width, height, x, y, heightScale, stepX, stepY));

                // The ramp texture is 256x1; U carries the value, V just picks the single row.
                textureCoordinates.Add(new Point(Math.Clamp((value + 1f) * 0.5f, 0f, 1f), 0.5));
            }
        }

        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int topLeft = x + (y * width);
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + width;
                int bottomRight = bottomLeft + 1;

                indices.Add(topLeft);
                indices.Add(bottomLeft);
                indices.Add(topRight);

                indices.Add(topRight);
                indices.Add(bottomLeft);
                indices.Add(bottomRight);
            }
        }

        MeshGeometry3D mesh = new()
        {
            Positions = positions,
            Normals = normals,
            TextureCoordinates = textureCoordinates,
            TriangleIndices = indices,
        };

        mesh.Freeze();
        return mesh;
    }

    /// <summary>
    /// Central-difference normal from the four neighbouring samples.
    /// </summary>
    /// <remarks>
    /// WPF will generate normals itself if none are supplied, but it averages face normals after
    /// the fact and the result creases along the triangulation. Taking them from the field instead
    /// gives smooth shading that actually follows the surface.
    /// </remarks>
    private static Vector3D EstimateNormal(
        ReadOnlySpan<float> field, int width, int height, int x, int y, float heightScale, float stepX, float stepY)
    {
        float left = field[Math.Max(0, x - 1) + (y * width)];
        float right = field[Math.Min(width - 1, x + 1) + (y * width)];
        float up = field[x + (Math.Max(0, y - 1) * width)];
        float down = field[x + (Math.Min(height - 1, y + 1) * width)];

        double spanX = (x > 0 && x < width - 1 ? 2.0 : 1.0) * stepX;
        double spanY = (y > 0 && y < height - 1 ? 2.0 : 1.0) * stepY;

        Vector3D normal = new(
            -(right - left) * heightScale / spanX,
            1.0,
            -(down - up) * heightScale / spanY);

        normal.Normalize();
        return normal;
    }
}
