using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainGridChunkBoundaryAlignmentPolicy
{
    internal static ImportedCityObject[] Align(IReadOnlyList<ImportedCityObject> cityObjects)
    {
        TerrainGridChunkAlignmentState?[] states = cityObjects
            .Select(static cityObject => TerrainGridChunkAlignmentState.TryCreate(cityObject))
            .ToArray();
        if (states.Any(static state => state is null))
        {
            return cityObjects.ToArray();
        }

        TerrainGridChunkAlignmentState[] chunkStates = states
            .Select(static state => state!)
            .ToArray();
        Dictionary<DemBoundarySampleKey, List<BoundaryHeightSampleReference>> sampleReferencesByKey = [];
        foreach (TerrainGridChunkAlignmentState state in chunkStates)
        {
            foreach (BoundaryHeightSampleReference sampleReference in EnumerateBoundaryHeightSampleReferences(state))
            {
                if (!sampleReferencesByKey.TryGetValue(sampleReference.Key, out List<BoundaryHeightSampleReference>? references))
                {
                    references = [];
                    sampleReferencesByKey.Add(sampleReference.Key, references);
                }

                references.Add(sampleReference);
            }
        }

        bool foundSharedBoundary = false;
        foreach (List<BoundaryHeightSampleReference> references in sampleReferencesByKey.Values)
        {
            if (references.Count < 2
                || references.Select(static reference => reference.State.CityObject).Distinct().Count() < 2)
            {
                continue;
            }

            foundSharedBoundary = true;
            BoundaryHeightSampleReference[] referencesToAlign = references
                .Where(static reference => reference.State.SampleCoverage[reference.SampleIndex] == TerrainGridSampleCoverage.Measured)
                .ToArray();
            if (referencesToAlign.Length == 0)
            {
                continue;
            }

            double alignedWorldHeight = referencesToAlign
                .Average(static reference => reference.State.BaseHeight + reference.State.HeightSamples[reference.SampleIndex]);
            foreach (BoundaryHeightSampleReference reference in referencesToAlign)
            {
                reference.State.HeightSamples[reference.SampleIndex] = alignedWorldHeight - reference.State.BaseHeight;
            }
        }

        if (!foundSharedBoundary)
        {
            return cityObjects.ToArray();
        }

        return chunkStates
            .Select(static state => state.ToCityObject())
            .ToArray();
    }

    private static IEnumerable<BoundaryHeightSampleReference> EnumerateBoundaryHeightSampleReferences(
        TerrainGridChunkAlignmentState state)
    {
        int width = state.Geometry.Width;
        int height = state.Geometry.Height;
        if (width < 2 || height < 2)
        {
            yield break;
        }

        for (int row = 0; row < height; row++)
        {
            yield return CreateBoundaryHeightSampleReference(state, row, 0);
            yield return CreateBoundaryHeightSampleReference(state, row, width - 1);
        }

        for (int column = 1; column < width - 1; column++)
        {
            yield return CreateBoundaryHeightSampleReference(state, 0, column);
            yield return CreateBoundaryHeightSampleReference(state, height - 1, column);
        }
    }

    private static BoundaryHeightSampleReference CreateBoundaryHeightSampleReference(
        TerrainGridChunkAlignmentState state,
        int row,
        int column)
    {
        double u = state.Geometry.Width == 1 ? 0.0 : (double)column / (state.Geometry.Width - 1);
        double v = state.Geometry.Height == 1 ? 0.0 : (double)row / (state.Geometry.Height - 1);
        double x = (state.CityObject.Transform.Position.X - (state.Geometry.Size.X / 2.0)) + (state.Geometry.Size.X * u);
        double z = (state.CityObject.Transform.Position.Z - (state.Geometry.Size.Y / 2.0)) + (state.Geometry.Size.Y * v);
        int sampleIndex = (row * state.Geometry.Width) + column;
        return new BoundaryHeightSampleReference(
            state,
            sampleIndex,
            new DemBoundarySampleKey(
                QuantizeBoundaryCoordinate(x),
                QuantizeBoundaryCoordinate(z)));
    }

    private static long QuantizeBoundaryCoordinate(double coordinate)
    {
        const double boundaryTolerance = 1e-3;
        return (long)Math.Round(coordinate / boundaryTolerance, MidpointRounding.AwayFromZero);
    }

    private sealed class TerrainGridChunkAlignmentState
    {
        public TerrainGridChunkAlignmentState(
            ImportedCityObject cityObject,
            TerrainGridGeometry geometry,
            double[] heightSamples)
        {
            CityObject = cityObject;
            Geometry = geometry;
            HeightSamples = heightSamples;
            SampleCoverage = geometry.SampleCoverage.ToArray();
            if (SampleCoverage.Length != heightSamples.Length)
            {
                throw new InvalidOperationException(
                    $"Terrain grid sample coverage count {SampleCoverage.Length} does not match height sample count {heightSamples.Length}.");
            }
            BaseHeight = cityObject.Transform.Position.Y - geometry.MaxHeight;
        }

        public ImportedCityObject CityObject { get; }

        public TerrainGridGeometry Geometry { get; }

        public double[] HeightSamples { get; }

        public TerrainGridSampleCoverage[] SampleCoverage { get; }

        public double BaseHeight { get; }

        public static TerrainGridChunkAlignmentState? TryCreate(ImportedCityObject cityObject)
        {
            TerrainGridGeometry? geometry = cityObject.Geometry switch
            {
                TerrainGridGeometry terrainGrid => terrainGrid,
                DynamicTerrainGeometry dynamicTerrain => dynamicTerrain.GridMesh,
                _ => null,
            };
            return geometry is not null
                ? new TerrainGridChunkAlignmentState(cityObject, geometry, geometry.HeightSamples.ToArray())
                : null;
        }

        public ImportedCityObject ToCityObject()
        {
            double minHeight = HeightSamples.Min();
            double maxHeight = HeightSamples.Max();
            Transform3D alignedTransform = CityObject.Transform with
            {
                Position = CityObject.Transform.Position with
                {
                    Y = BaseHeight + maxHeight,
                },
            };

            return CityObject with
            {
                Transform = alignedTransform,
                Geometry = CityObject.Geometry switch
                {
                    DynamicTerrainGeometry dynamicTerrain => dynamicTerrain with
                    {
                        StaticMesh = TriangleMeshTransformRebaser.Rebase(
                            dynamicTerrain.StaticMesh,
                            CityObject.Transform,
                            alignedTransform),
                        GridMesh = dynamicTerrain.GridMesh with
                        {
                            MinHeight = minHeight,
                            MaxHeight = maxHeight,
                            HeightSamples = HeightSamples,
                        },
                    },
                    _ => Geometry with
                    {
                        MinHeight = minHeight,
                        MaxHeight = maxHeight,
                        HeightSamples = HeightSamples,
                    },
                },
            };
        }
    }

    private sealed record DemBoundarySampleKey(
        long QuantizedX,
        long QuantizedZ);

    private sealed record BoundaryHeightSampleReference(
        TerrainGridChunkAlignmentState State,
        int SampleIndex,
        DemBoundarySampleKey Key);
}
