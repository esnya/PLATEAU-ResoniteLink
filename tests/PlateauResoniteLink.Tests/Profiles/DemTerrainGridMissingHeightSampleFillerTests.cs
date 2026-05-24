using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemTerrainGridMissingHeightSampleFillerTests
{
    [Fact]
    public void ExtendBoundaryConnectedMissingSamplesFillsBoundaryBandWithoutChangingInteriorHole()
    {
        const int width = 5;
        const int height = 5;
        const double fallbackHeight = -1000.0;
        double[] localHeights = new double[width * height];
        bool[] sampledInsideTriangles = new bool[width * height];

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int sampleIndex = (row * width) + column;
                localHeights[sampleIndex] = (row * 10.0) + column;
                sampledInsideTriangles[sampleIndex] = true;
            }
        }

        for (int row = 0; row < height; row++)
        {
            int missingBoundarySampleIndex = row * width;
            localHeights[missingBoundarySampleIndex] = fallbackHeight;
            sampledInsideTriangles[missingBoundarySampleIndex] = false;
        }

        int interiorHoleIndex = (2 * width) + 2;
        localHeights[interiorHoleIndex] = fallbackHeight;
        sampledInsideTriangles[interiorHoleIndex] = false;

        DemTerrainGridMissingHeightSampleFiller.ExtendBoundaryConnectedMissingSamples(
            localHeights,
            sampledInsideTriangles,
            width,
            height);

        for (int row = 0; row < height; row++)
        {
            Assert.Equal((row * 10.0) + 1.0, localHeights[row * width]);
            Assert.True(sampledInsideTriangles[row * width]);
        }

        Assert.Equal(fallbackHeight, localHeights[interiorHoleIndex]);
        Assert.False(sampledInsideTriangles[interiorHoleIndex]);
    }
}
