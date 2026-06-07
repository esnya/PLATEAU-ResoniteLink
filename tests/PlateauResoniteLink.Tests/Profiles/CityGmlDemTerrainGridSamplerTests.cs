using System;
using System.Collections.Generic;
using System.Threading;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityGmlDemTerrainGridSamplerTests
{
    [Fact]
    public void SampleInterpolatesHeightsInsideTriangles()
    {
        TerrainGridTriangle[] triangles =
        [
            new(
                new Float3(0.0, 10.0, 0.0),
                new Float3(1.0, 20.0, 0.0),
                new Float3(0.0, 30.0, 1.0)),
            new(
                new Float3(1.0, 20.0, 0.0),
                new Float3(1.0, 40.0, 1.0),
                new Float3(0.0, 30.0, 1.0)),
        ];

        DemTerrainGridHeightSamples samples = CityGmlDemTerrainGridSampler.Sample(
            minX: 0.0,
            maxX: 1.0,
            minZ: 0.0,
            maxZ: 1.0,
            metersPerVertex: 0.5,
            maxResolution: 3,
            fallbackHeight: -100.0,
            triangles);

        Assert.Equal(3, samples.Width);
        Assert.Equal(3, samples.Height);
        Assert.Equal(10.0, samples.LocalHeights[0], precision: 6);
        Assert.Equal(15.0, samples.LocalHeights[1], precision: 6);
        Assert.Equal(20.0, samples.LocalHeights[2], precision: 6);
        Assert.Equal(20.0, samples.LocalHeights[3], precision: 6);
        Assert.Equal(25.0, samples.LocalHeights[4], precision: 6);
        Assert.Equal(30.0, samples.LocalHeights[6], precision: 6);
        Assert.Equal(40.0, samples.LocalHeights[8], precision: 6);
        Assert.All(samples.SampleCoverage, static coverage => Assert.Equal(TerrainGridSampleCoverage.Measured, coverage));
    }

    [Fact]
    public void SampleUsesGridVerticesForSurfaceCoveredEdges()
    {
        TerrainGridTriangle[] triangles =
        [
            new(
                new Float3(0.0, 10.0, 0.0),
                new Float3(2.0, 20.0, 0.0),
                new Float3(0.0, 30.0, 2.0)),
            new(
                new Float3(2.0, 20.0, 0.0),
                new Float3(2.0, 40.0, 2.0),
                new Float3(0.0, 30.0, 2.0)),
        ];

        DemTerrainGridHeightSamples samples = CityGmlDemTerrainGridSampler.Sample(
            minX: 0.0,
            maxX: 2.0,
            minZ: 0.0,
            maxZ: 2.0,
            metersPerVertex: 1.0,
            maxResolution: 3,
            fallbackHeight: -100.0,
            triangles);

        Assert.Equal(10.0, samples.LocalHeights[0], precision: 6);
        Assert.Equal(20.0, samples.LocalHeights[2], precision: 6);
        Assert.Equal(25.0, samples.LocalHeights[4], precision: 6);
        Assert.Equal(30.0, samples.LocalHeights[6], precision: 6);
        Assert.Equal(40.0, samples.LocalHeights[8], precision: 6);
        Assert.DoesNotContain(samples.LocalHeights, sample => Math.Abs(sample - -100.0) <= 1e-6);
        Assert.All(samples.SampleCoverage, static coverage => Assert.Equal(TerrainGridSampleCoverage.Measured, coverage));
    }

    [Fact]
    public void SampleKeepsSurfaceMissingSamplesAtSeaLevelWithCoverage()
    {
        TerrainGridTriangle[] triangles =
        [
            new(
                new Float3(0.0, 10.0, 0.0),
                new Float3(2.0, 20.0, 0.0),
                new Float3(0.0, 30.0, 2.0)),
        ];

        DemTerrainGridHeightSamples samples = CityGmlDemTerrainGridSampler.Sample(
            minX: 0.0,
            maxX: 2.0,
            minZ: 0.0,
            maxZ: 2.0,
            metersPerVertex: 1.0,
            maxResolution: 3,
            fallbackHeight: -100.0,
            triangles);

        Assert.Equal(10.0, samples.LocalHeights[0], precision: 6);
        Assert.Equal(25.0, samples.LocalHeights[4], precision: 6);
        Assert.Equal(-100.0, samples.LocalHeights[5], precision: 6);
        Assert.Equal(-100.0, samples.LocalHeights[7], precision: 6);
        Assert.Equal(-100.0, samples.LocalHeights[8], precision: 6);
        Assert.Equal(TerrainGridSampleCoverage.NoSurface, samples.SampleCoverage[5]);
        Assert.Equal(TerrainGridSampleCoverage.NoSurface, samples.SampleCoverage[7]);
        Assert.Equal(TerrainGridSampleCoverage.NoSurface, samples.SampleCoverage[8]);
    }

    [Fact]
    public void SampleSeparatesMeasuredSeaLevelFromNoSurfaceSeaLevel()
    {
        TerrainGridTriangle[] triangles =
        [
            new(
                new Float3(0.0, 0.0, 0.0),
                new Float3(1.0, 0.0, 0.0),
                new Float3(0.0, 0.0, 1.0)),
        ];

        DemTerrainGridHeightSamples samples = CityGmlDemTerrainGridSampler.Sample(
            minX: 0.0,
            maxX: 2.0,
            minZ: 0.0,
            maxZ: 2.0,
            metersPerVertex: 1.0,
            maxResolution: 3,
            fallbackHeight: 0.0,
            triangles);

        Assert.Equal(0.0, samples.LocalHeights[0], precision: 6);
        Assert.Equal(0.0, samples.LocalHeights[8], precision: 6);
        Assert.Equal(TerrainGridSampleCoverage.Measured, samples.SampleCoverage[0]);
        Assert.Equal(TerrainGridSampleCoverage.NoSurface, samples.SampleCoverage[8]);
    }

    [Fact]
    public void SampleHonorsCancellationAfterSamplingBeforeBoundaryFill()
    {
        using CancellationTokenSource cancellation = new();
        CancelingTriangleList triangles = new(
            new TerrainGridTriangle(
                new Float3(-1.0, 10.0, -1.0),
                new Float3(3.0, 10.0, -1.0),
                new Float3(-1.0, 10.0, 3.0)),
            cancellation,
            cancelAfterReadCount: 10);

        Assert.Throws<OperationCanceledException>(() =>
            CityGmlDemTerrainGridSampler.Sample(
                minX: 0.0,
                maxX: 1.0,
                minZ: 0.0,
                maxZ: 1.0,
                metersPerVertex: 0.5,
                maxResolution: 3,
                fallbackHeight: -100.0,
                triangles,
                cancellation.Token));
    }

    private sealed class CancelingTriangleList(
        TerrainGridTriangle triangle,
        CancellationTokenSource cancellation,
        int cancelAfterReadCount) : IReadOnlyList<TerrainGridTriangle>
    {
        private int readCount;

        public int Count => 1;

        public TerrainGridTriangle this[int index]
        {
            get
            {
                Assert.Equal(0, index);
                if (++readCount == cancelAfterReadCount)
                {
                    cancellation.Cancel();
                }

                return triangle;
            }
        }

        public IEnumerator<TerrainGridTriangle> GetEnumerator()
        {
            yield return triangle;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
