using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainGridChunkBoundaryAligner
{
    public static ImportedCityObject[] AlignAdjacentBoundaries(IReadOnlyList<ImportedCityObject> cityObjects)
    {
        ArgumentNullException.ThrowIfNull(cityObjects);

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
        const double seaLevelWorldHeightTolerance = 1e-6;
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
            double worldHeightSum = 0.0;
            int sampleCount = 0;
            double nonSeaLevelWorldHeightSum = 0.0;
            int nonSeaLevelSampleCount = 0;
            foreach (BoundaryHeightSampleReference reference in references)
            {
                double worldHeight = reference.State.BaseHeight + reference.State.HeightSamples[reference.SampleIndex];
                bool isSeaLevelFallbackCandidate = Math.Abs(worldHeight) <= seaLevelWorldHeightTolerance;
                worldHeightSum += worldHeight;
                sampleCount++;
                if (!isSeaLevelFallbackCandidate)
                {
                    nonSeaLevelWorldHeightSum += worldHeight;
                    nonSeaLevelSampleCount++;
                }
            }

            double alignedWorldHeight = nonSeaLevelSampleCount > 0
                ? nonSeaLevelWorldHeightSum / nonSeaLevelSampleCount
                : worldHeightSum / sampleCount;
            foreach (BoundaryHeightSampleReference reference in references)
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

    public static TriangleMeshGeometry RebaseTriangleMeshToTransform(
        TriangleMeshGeometry source,
        Transform3D sourceTransform,
        Transform3D targetTransform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceTransform);
        ArgumentNullException.ThrowIfNull(targetTransform);

        ImportedMesh mesh = source.Mesh;
        MeshVertex[] vertices = mesh.Vertices
            .Select(vertex =>
            {
                Float3 worldPosition = TransformPointToWorld(sourceTransform, vertex.Position);
                Float3 localPosition = TransformVectorFromWorld(targetTransform, Subtract(worldPosition, targetTransform.Position));
                Float3 worldNormal = sourceTransform.Rotation is null ? vertex.Normal : Rotate(vertex.Normal, sourceTransform.Rotation);
                Float3 localNormal = TransformVectorFromWorld(targetTransform, worldNormal);
                return vertex with
                {
                    Position = localPosition,
                    Normal = localNormal,
                };
            })
            .ToArray();
        return new TriangleMeshGeometry(new ImportedMesh(vertices, mesh.Submeshes));
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

    private static Float3 TransformPointToWorld(Transform3D transform, Float3 localPosition)
    {
        Float3 rotated = transform.Rotation is null
            ? localPosition
            : Rotate(localPosition, transform.Rotation);
        return Add(transform.Position, rotated);
    }

    private static Float3 TransformVectorFromWorld(Transform3D transform, Float3 worldVector)
    {
        return transform.Rotation is null
            ? worldVector
            : Rotate(worldVector, Conjugate(transform.Rotation));
    }

    private static Float3 Rotate(Float3 value, Quaternion rotation)
    {
        Float3 qv = new(rotation.X, rotation.Y, rotation.Z);
        Float3 uv = Cross(qv, value);
        Float3 uuv = Cross(qv, uv);
        return Add(
            value,
            Add(
                Scale(uv, 2.0 * rotation.W),
                Scale(uuv, 2.0)));
    }

    private static Quaternion Conjugate(Quaternion value)
    {
        return new Quaternion(-value.X, -value.Y, -value.Z, value.W);
    }

    private static Float3 Add(Float3 left, Float3 right)
    {
        return new Float3(
            left.X + right.X,
            left.Y + right.Y,
            left.Z + right.Z);
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);
    }

    private static Float3 Cross(Float3 left, Float3 right)
    {
        return new Float3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static Float3 Scale(Float3 value, double scalar)
    {
        return new Float3(value.X * scalar, value.Y * scalar, value.Z * scalar);
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
            BaseHeight = cityObject.Transform.Position.Y - geometry.MaxHeight;
        }

        public ImportedCityObject CityObject { get; }

        public TerrainGridGeometry Geometry { get; }

        public double[] HeightSamples { get; }

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
                        StaticMesh = RebaseTriangleMeshToTransform(
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
