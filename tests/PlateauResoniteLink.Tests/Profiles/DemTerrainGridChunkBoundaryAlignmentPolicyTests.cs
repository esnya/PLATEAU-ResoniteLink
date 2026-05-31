using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemTerrainGridChunkBoundaryAlignmentPolicyTests
{
    [Fact]
    public void AlignAveragesSharedSamplesForPartialOverlapWithDifferentResolution()
    {
        ImportedCityObject left = CreateTerrainGridCityObject(
            "left-dem",
            new Float3(0.0, 14.0, 0.0),
            width: 2,
            height: 5,
            sizeX: 2.0,
            sizeZ: 4.0,
            [
                1.0, 10.0,
                1.0, 11.0,
                1.0, 12.0,
                1.0, 13.0,
                1.0, 14.0,
            ]);
        ImportedCityObject right = CreateTerrainGridCityObject(
            "right-dem",
            new Float3(2.0, 22.0, 0.0),
            width: 2,
            height: 3,
            sizeX: 2.0,
            sizeZ: 2.0,
            [
                20.0, 2.0,
                21.0, 2.0,
                22.0, 2.0,
            ]);

        ImportedCityObject[] aligned = DemTerrainGridChunkBoundaryAlignmentPolicy.Align([left, right]);

        TerrainGridGeometry alignedLeft = Assert.IsType<TerrainGridGeometry>(aligned[0].Geometry);
        TerrainGridGeometry alignedRight = Assert.IsType<TerrainGridGeometry>(aligned[1].Geometry);

        Assert.Equal(10.0, alignedLeft.HeightSamples[1], 6);
        Assert.Equal(15.5, alignedLeft.HeightSamples[3], 6);
        Assert.Equal(16.5, alignedLeft.HeightSamples[5], 6);
        Assert.Equal(17.5, alignedLeft.HeightSamples[7], 6);
        Assert.Equal(14.0, alignedLeft.HeightSamples[9], 6);

        Assert.Equal(15.5, alignedRight.HeightSamples[0], 6);
        Assert.Equal(16.5, alignedRight.HeightSamples[2], 6);
        Assert.Equal(17.5, alignedRight.HeightSamples[4], 6);
    }

    [Fact]
    public void AlignRebasesDynamicTerrainStaticMeshWhenGridTransformChanges()
    {
        ImportedCityObject left = CreateDynamicTerrainCityObject(
            "left-dem",
            new Float3(0.0, 14.0, 0.0),
            width: 2,
            height: 2,
            sizeX: 2.0,
            sizeZ: 2.0,
            [
                1.0, 10.0,
                1.0, 14.0,
            ]);
        ImportedCityObject right = CreateTerrainGridCityObject(
            "right-dem",
            new Float3(2.0, 22.0, 0.0),
            width: 2,
            height: 2,
            sizeX: 2.0,
            sizeZ: 2.0,
            [
                20.0, 2.0,
                22.0, 2.0,
            ]);
        double originalWorldY = left.Transform.Position.Y
            + Assert.IsType<DynamicTerrainGeometry>(left.Geometry).StaticMesh.Mesh.Vertices[0].Position.Y;

        ImportedCityObject[] aligned = DemTerrainGridChunkBoundaryAlignmentPolicy.Align([left, right]);

        DynamicTerrainGeometry alignedGeometry = Assert.IsType<DynamicTerrainGeometry>(aligned[0].Geometry);
        double alignedWorldY = aligned[0].Transform.Position.Y
            + alignedGeometry.StaticMesh.Mesh.Vertices[0].Position.Y;
        Assert.Equal(originalWorldY, alignedWorldY, precision: 6);
        Assert.NotEqual(left.Transform.Position.Y, aligned[0].Transform.Position.Y);
    }

    [Fact]
    public void AlignUsesMeasuredCoverageInsteadOfSeaLevelSentinel()
    {
        ImportedCityObject left = CreateTerrainGridCityObject(
            "left-dem",
            new Float3(0.0, 1.0, 0.0),
            width: 2,
            height: 2,
            sizeX: 2.0,
            sizeZ: 2.0,
            [1.0, 0.0, 1.0, 0.0],
            [Measured, Measured, Measured, Measured]);
        ImportedCityObject right = CreateTerrainGridCityObject(
            "right-dem",
            new Float3(2.0, 9.0, 0.0),
            width: 2,
            height: 2,
            sizeX: 2.0,
            sizeZ: 2.0,
            [4.0, 9.0, 4.0, 9.0],
            [Measured, Measured, Measured, Measured]);

        ImportedCityObject[] aligned = DemTerrainGridChunkBoundaryAlignmentPolicy.Align([left, right]);

        TerrainGridGeometry alignedLeft = Assert.IsType<TerrainGridGeometry>(aligned[0].Geometry);
        TerrainGridGeometry alignedRight = Assert.IsType<TerrainGridGeometry>(aligned[1].Geometry);
        Assert.Equal(2.0, alignedLeft.HeightSamples[1], 6);
        Assert.Equal(2.0, alignedLeft.HeightSamples[3], 6);
        Assert.Equal(2.0, alignedRight.HeightSamples[0], 6);
        Assert.Equal(2.0, alignedRight.HeightSamples[2], 6);
    }

    [Fact]
    public void AlignDoesNotRaiseNoSurfaceBoundarySamplesToMeasuredHeight()
    {
        ImportedCityObject left = CreateTerrainGridCityObject(
            "left-dem",
            new Float3(0.0, 10.0, 0.0),
            width: 2,
            height: 2,
            sizeX: 2.0,
            sizeZ: 2.0,
            [1.0, 10.0, 1.0, 10.0],
            [Measured, Measured, Measured, Measured]);
        ImportedCityObject right = CreateTerrainGridCityObject(
            "right-dem",
            new Float3(2.0, 2.0, 0.0),
            width: 2,
            height: 2,
            sizeX: 2.0,
            sizeZ: 2.0,
            [0.0, 2.0, 0.0, 2.0],
            [NoSurface, Measured, NoSurface, Measured]);

        ImportedCityObject[] aligned = DemTerrainGridChunkBoundaryAlignmentPolicy.Align([left, right]);

        TerrainGridGeometry alignedLeft = Assert.IsType<TerrainGridGeometry>(aligned[0].Geometry);
        TerrainGridGeometry alignedRight = Assert.IsType<TerrainGridGeometry>(aligned[1].Geometry);
        Assert.Equal(10.0, alignedLeft.HeightSamples[1], 6);
        Assert.Equal(10.0, alignedLeft.HeightSamples[3], 6);
        Assert.Equal(0.0, alignedRight.HeightSamples[0], 6);
        Assert.Equal(0.0, alignedRight.HeightSamples[2], 6);
    }

    private static ImportedCityObject CreateDynamicTerrainCityObject(
        string slotKey,
        Float3 position,
        int width,
        int height,
        double sizeX,
        double sizeZ,
        IReadOnlyList<double> heightSamples)
    {
        ImportedCityObject grid = CreateTerrainGridCityObject(
            slotKey,
            position,
            width,
            height,
            sizeX,
            sizeZ,
            heightSamples);
        TriangleMeshGeometry staticMesh = new(new ImportedMesh(
            [
                new MeshVertex(
                    new Float3(0.0, 1.0, 0.0),
                    new Float3(0.0, 1.0, 0.0),
                    new Float2(0.0, 0.0)),
            ],
            [new MeshSubmesh(0, [])]));
        return grid with
        {
            Geometry = new DynamicTerrainGeometry(staticMesh, Assert.IsType<TerrainGridGeometry>(grid.Geometry)),
        };
    }

    private static ImportedCityObject CreateTerrainGridCityObject(
        string slotKey,
        Float3 position,
        int width,
        int height,
        double sizeX,
        double sizeZ,
        IReadOnlyList<double> heightSamples,
        IReadOnlyList<TerrainGridSampleCoverage>? sampleCoverage = null)
    {
        MaterialBinding material = new(
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);
        return new ImportedCityObject(
            ObjectKey: slotKey,
            DisplayName: slotKey,
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new Transform3D(position),
            Geometry: new TerrainGridGeometry(
                Width: width,
                Height: height,
                Size: new Float2(sizeX, sizeZ),
                MinHeight: heightSamples.Min(),
                MaxHeight: heightSamples.Max(),
                HeightSamples: heightSamples,
                SampleCoverage: sampleCoverage ?? CreateMeasuredCoverage(heightSamples.Count)),
            Materials: [material],
            SourceFileRelativePath: $"udx/dem/53394525/{slotKey}.gml");
    }

    private static TerrainGridSampleCoverage[] CreateMeasuredCoverage(int count)
    {
        return Enumerable.Repeat(TerrainGridSampleCoverage.Measured, count).ToArray();
    }

    private const TerrainGridSampleCoverage Measured = TerrainGridSampleCoverage.Measured;

    private const TerrainGridSampleCoverage NoSurface = TerrainGridSampleCoverage.NoSurface;
}
