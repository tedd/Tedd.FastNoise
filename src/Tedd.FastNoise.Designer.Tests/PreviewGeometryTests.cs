using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;
using Tedd.FastNoise;
using Tedd.FastNoise.Designer.CodeGen;
using Tedd.FastNoise.Designer.Presets;
using Tedd.FastNoise.Designer.Rendering;
using Tedd.FastNoise.Designer.ViewModels;

namespace Tedd.FastNoise.Designer.Tests;

public sealed class PreviewGeometryTests
{
    [Fact]
    public void Centered2D_ZoomChangesExtentButNotCenter()
    {
        const float centerX = 125f;
        const float centerY = -75f;
        const int edge = 384;

        foreach (float step in new[] { 0.25f, 1f, 64f, 4096f })
        {
            PreviewRegion region = PreviewRegion.Centered2D(centerX, centerY, edge, edge, step);

            AssertClose(centerX, AxisCenter(region.OriginX, region.Width, region.Step));
            AssertClose(centerY, AxisCenter(region.OriginY, region.Height, region.Step));
        }
    }

    [Fact]
    public void Centered3D_UsesEveryAxisDimension()
    {
        PreviewRegion region = PreviewRegion.Centered3D(
            centerX: 12f,
            centerY: -34f,
            centerZ: 56f,
            width: 39,
            height: 40,
            depth: 41,
            step: 16f);

        AssertClose(12f, AxisCenter(region.OriginX, region.Width, region.Step));
        AssertClose(-34f, AxisCenter(region.OriginY, region.Height, region.Step));
        AssertClose(56f, AxisCenter(region.OriginZ, region.Depth, region.Step));
    }

    [Fact]
    public void Heightmap_ZoomScalesDisplacementAndNormals()
    {
        float[] field =
        [
            -1f, 0f, 1f,
            -1f, 0f, 1f,
            -1f, 0f, 1f,
        ];

        MeshGeometry3D near = HeightmapMesh.Build(
            field, 3, 3, heightScale: 0.5f, worldUnitsPerSample: 1f);
        MeshGeometry3D far = HeightmapMesh.Build(
            field, 3, 3, heightScale: 0.5f, worldUnitsPerSample: 4f);

        double nearSpan = near.Positions.Max(static point => point.Y) - near.Positions.Min(static point => point.Y);
        double farSpan = far.Positions.Max(static point => point.Y) - far.Positions.Min(static point => point.Y);

        AssertClose(1.0, nearSpan);
        AssertClose(0.25, farSpan);
        AssertClose(nearSpan, farSpan * 4.0);

        // The centre normal follows the same scaled surface, so distant terrain is also lit flatter.
        AssertClose(1.0 / Math.Sqrt(2.0), near.Normals[4].Y);
        AssertClose(1.0 / Math.Sqrt(1.0625), far.Normals[4].Y);
    }

    [Fact]
    public void Downsample_PreservesTheSourceEndpoints()
    {
        float[] source = Enumerable.Range(0, 25).Select(static value => (float)value).ToArray();

        float[] result = MainViewModel.Downsample(source, sourceEdge: 5, targetEdge: 3);

        Assert.Equal(
            new float[]
            {
                0f, 2f, 4f,
                10f, 12f, 14f,
                20f, 22f, 24f,
            },
            result);
    }

    [Fact]
    public void GeneratedCode_UsesTheResolvedFirstSample()
    {
        PreviewRegion region = PreviewRegion.Centered2D(
            centerX: 10f, centerY: -20f, width: 4, height: 6, step: 2f);

        string code = CSharpEmitter.Emit(
            [new LayerViewModel()], LodPolicy.Disabled, region, isVolume: false);

        Assert.Contains("    7f, -25f,", code);
    }

    [Fact]
    public void Version2Design_RoundTripsCentersWithoutLegacyOrigins()
    {
        DesignDocument document = new()
        {
            View = new ViewDocument
            {
                CenterX = 12.5f,
                CenterY = -25f,
                CenterZ = 50f,
            },
        };

        string path = TemporaryDesignPath();

        try
        {
            document.Save(path);
            string json = File.ReadAllText(path);
            DesignDocument loaded = DesignDocument.Load(path);
            (float centerX, float centerY, float centerZ) = MainViewModel.ResolveSavedCenter(
                loaded,
                loaded.View.Mode,
                loaded.View.Resolution,
                loaded.View.VolumeResolution,
                loaded.View.Step);

            Assert.Contains("\"Version\": 2", json);
            Assert.DoesNotContain("\"OriginX\"", json);
            Assert.Equal(12.5f, centerX);
            Assert.Equal(-25f, centerY);
            Assert.Equal(50f, centerZ);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Version1Design_LoadsOriginsForCenterMigration()
    {
        const string json =
            """
            {
              "Version": 1,
              "Layers": [],
              "View": {
                "Mode": "Map2D",
                "OriginX": 10,
                "OriginY": -20,
                "OriginZ": 5,
                "Step": 2,
                "Resolution": 4,
                "VolumeResolution": 3
              }
            }
            """;

        string path = TemporaryDesignPath();

        try
        {
            File.WriteAllText(path, json);
            DesignDocument loaded = DesignDocument.Load(path);
            (float centerX, float centerY, float centerZ) = MainViewModel.ResolveSavedCenter(
                loaded,
                loaded.View.Mode,
                loaded.View.Resolution,
                loaded.View.VolumeResolution,
                loaded.View.Step);

            Assert.Equal(1, loaded.Version);
            AssertClose(13f, centerX);
            AssertClose(-17f, centerY);
            AssertClose(7f, centerZ);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static double AxisCenter(float origin, int count, float step)
        => origin + (((count - 1) * step) * 0.5);

    private static void AssertClose(double expected, double actual)
        => Assert.InRange(Math.Abs(expected - actual), 0.0, 0.001);

    private static string TemporaryDesignPath()
        => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.fnoise.json");
}
