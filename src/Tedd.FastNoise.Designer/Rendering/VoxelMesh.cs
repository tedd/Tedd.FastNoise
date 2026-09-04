using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Tedd.FastNoise.Designer.Rendering;

/// <summary>
/// Turns a 3D density field into the voxel world it describes: cells at or above the threshold are
/// solid, and only the faces between a solid cell and an empty one are emitted.
/// </summary>
/// <remarks>
/// <para>
/// Face culling is the whole trick. A 48-cube filled to a third is around 55,000 solid cells and
/// 330,000 faces if drawn naively; keeping only the exposed ones typically leaves under 10% of
/// that, which is the difference between a preview that rotates smoothly and one that does not.
/// </para>
/// <para>
/// One mesh, one material. Per-face colour comes from a texture coordinate into the 1D ramp,
/// so the whole volume is a single draw.
/// </para>
/// </remarks>
public static class VoxelMesh
{
    /// <summary>Face directions, each with its outward normal and the four corner offsets of the quad.</summary>
    private static readonly (int Dx, int Dy, int Dz, Vector3D Normal, (int X, int Y, int Z)[] Corners)[] Faces =
    [
        (1, 0, 0, new Vector3D(1, 0, 0), [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)]),
        (-1, 0, 0, new Vector3D(-1, 0, 0), [(0, 0, 1), (0, 1, 1), (0, 1, 0), (0, 0, 0)]),
        (0, 1, 0, new Vector3D(0, 1, 0), [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)]),
        (0, -1, 0, new Vector3D(0, -1, 0), [(0, 0, 1), (0, 0, 0), (1, 0, 0), (1, 0, 1)]),
        (0, 0, 1, new Vector3D(0, 0, 1), [(1, 0, 1), (1, 1, 1), (0, 1, 1), (0, 0, 1)]),
        (0, 0, -1, new Vector3D(0, 0, -1), [(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)]),
    ];

    /// <summary>Builds the mesh.</summary>
    /// <param name="field">Sample values, X-fastest then Y then Z.</param>
    /// <param name="width">Samples along X.</param>
    /// <param name="height">Samples along Y.</param>
    /// <param name="depth">Samples along Z.</param>
    /// <param name="threshold">Values at or above this count as solid.</param>
    /// <param name="faceCount">Receives how many faces were emitted, for the status readout.</param>
    /// <returns>A frozen mesh spanning roughly [-0.5, 0.5] on its longest axis.</returns>
    public static MeshGeometry3D Build(
        ReadOnlySpan<float> field, int width, int height, int depth, float threshold, out int faceCount)
    {
        Point3DCollection positions = [];
        Vector3DCollection normals = [];
        PointCollection textureCoordinates = [];
        Int32Collection indices = [];

        // Normalise the longest axis to 1 so the camera framing does not depend on the volume shape.
        double scale = 1.0 / Math.Max(width, Math.Max(height, depth));
        double centreX = width * 0.5;
        double centreY = height * 0.5;
        double centreZ = depth * 0.5;

        faceCount = 0;

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value = field[x + (width * (y + (height * z)))];
                    if (value < threshold)
                    {
                        continue;
                    }

                    foreach ((int dx, int dy, int dz, Vector3D normal, (int X, int Y, int Z)[] corners) in Faces)
                    {
                        if (IsSolid(field, width, height, depth, threshold, x + dx, y + dy, z + dz))
                        {
                            continue;
                        }

                        int firstVertex = positions.Count;
                        double u = Math.Clamp((value + 1f) * 0.5f, 0f, 1f);

                        foreach ((int cornerX, int cornerY, int cornerZ) in corners)
                        {
                            positions.Add(new Point3D(
                                (x + cornerX - centreX) * scale,
                                (y + cornerY - centreY) * scale,
                                (z + cornerZ - centreZ) * scale));

                            normals.Add(normal);
                            textureCoordinates.Add(new Point(u, 0.5));
                        }

                        indices.Add(firstVertex);
                        indices.Add(firstVertex + 1);
                        indices.Add(firstVertex + 2);

                        indices.Add(firstVertex);
                        indices.Add(firstVertex + 2);
                        indices.Add(firstVertex + 3);

                        faceCount++;
                    }
                }
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

    /// <summary>Whether a cell is solid. Cells outside the volume count as empty, so the outer shell is drawn.</summary>
    private static bool IsSolid(
        ReadOnlySpan<float> field, int width, int height, int depth, float threshold, int x, int y, int z)
        => x >= 0 && x < width
            && y >= 0 && y < height
            && z >= 0 && z < depth
            && field[x + (width * (y + (height * z)))] >= threshold;
}
