using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainGridMissingHeightSampleFiller
{
    public static void ExtendBoundaryConnectedMissingSamples(
        double[] localHeights,
        bool[] sampledInsideTriangles,
        int width,
        int height)
    {
        bool[] boundaryConnectedMissing = FindBoundaryConnectedMissingSamples(sampledInsideTriangles, width, height);
        if (!boundaryConnectedMissing.Any(static missing => missing))
        {
            return;
        }

        Queue<(int Row, int Column)> frontier = new();

        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int sampleIndex = (row * width) + column;
                if (!sampledInsideTriangles[sampleIndex])
                {
                    continue;
                }

                if (TouchesBoundaryConnectedMissing(row, column))
                {
                    frontier.Enqueue((row, column));
                }
            }
        }

        while (frontier.Count > 0)
        {
            (int row, int column) = frontier.Dequeue();
            int sourceIndex = (row * width) + column;
            TryPropagate(row - 1, column, localHeights[sourceIndex]);
            TryPropagate(row + 1, column, localHeights[sourceIndex]);
            TryPropagate(row, column - 1, localHeights[sourceIndex]);
            TryPropagate(row, column + 1, localHeights[sourceIndex]);
        }

        bool TouchesBoundaryConnectedMissing(int row, int column)
        {
            return IsBoundaryConnectedMissing(row - 1, column)
                || IsBoundaryConnectedMissing(row + 1, column)
                || IsBoundaryConnectedMissing(row, column - 1)
                || IsBoundaryConnectedMissing(row, column + 1);
        }

        bool IsBoundaryConnectedMissing(int row, int column)
        {
            if ((uint)row >= (uint)height || (uint)column >= (uint)width)
            {
                return false;
            }

            return boundaryConnectedMissing[(row * width) + column];
        }

        void TryPropagate(int row, int column, double heightValue)
        {
            if ((uint)row >= (uint)height || (uint)column >= (uint)width)
            {
                return;
            }

            int targetIndex = (row * width) + column;
            if (!boundaryConnectedMissing[targetIndex] || sampledInsideTriangles[targetIndex])
            {
                return;
            }

            localHeights[targetIndex] = heightValue;
            sampledInsideTriangles[targetIndex] = true;
            frontier.Enqueue((row, column));
        }
    }

    private static bool[] FindBoundaryConnectedMissingSamples(
        bool[] sampledInsideTriangles,
        int width,
        int height)
    {
        bool[] boundaryConnectedMissing = new bool[width * height];
        Queue<(int Row, int Column)> frontier = new();

        for (int column = 0; column < width; column++)
        {
            EnqueueIfBoundaryMissing(0, column);
            EnqueueIfBoundaryMissing(height - 1, column);
        }

        for (int row = 1; row < height - 1; row++)
        {
            EnqueueIfBoundaryMissing(row, 0);
            EnqueueIfBoundaryMissing(row, width - 1);
        }

        while (frontier.Count > 0)
        {
            (int row, int column) = frontier.Dequeue();
            TryVisit(row - 1, column);
            TryVisit(row + 1, column);
            TryVisit(row, column - 1);
            TryVisit(row, column + 1);
        }

        return boundaryConnectedMissing;

        void EnqueueIfBoundaryMissing(int row, int column)
        {
            if ((uint)row >= (uint)height || (uint)column >= (uint)width)
            {
                return;
            }

            int sampleIndex = (row * width) + column;
            if (sampledInsideTriangles[sampleIndex] || boundaryConnectedMissing[sampleIndex])
            {
                return;
            }

            boundaryConnectedMissing[sampleIndex] = true;
            frontier.Enqueue((row, column));
        }

        void TryVisit(int row, int column)
        {
            if ((uint)row >= (uint)height || (uint)column >= (uint)width)
            {
                return;
            }

            int sampleIndex = (row * width) + column;
            if (sampledInsideTriangles[sampleIndex] || boundaryConnectedMissing[sampleIndex])
            {
                return;
            }

            boundaryConnectedMissing[sampleIndex] = true;
            frontier.Enqueue((row, column));
        }
    }
}
