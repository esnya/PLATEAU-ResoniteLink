using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteCityObjectWorkingSetEstimator
{
    private const long MinimumWeightBytes = 16L * 1024L * 1024L;
    private const long TextureReferenceWeightBytes = 16L * 1024L * 1024L;
    private const long HeightSampleWeightBytes = sizeof(double);
    private const long HdrHeightTextureWeightBytes = 4L * sizeof(float);
    private const long MaterialBindingWeightBytes = 4096L;
    private const long VertexWeightBytes = 256L;
    private const long IndexWeightBytes = 16L;
    private const long PerSubmeshWeightBytes = 4096L;
    private const long TriangleMeshExpansionFactor = 4L;
    private const long HeightMapExpansionFactor = 2L;

    public static long Estimate(ResoniteConstructionCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        long geometryWeightBytes = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => checked(
                EstimateTriangleMeshWorkingSetBytes(triangleMesh.Mesh, cityObject.Materials) * TriangleMeshExpansionFactor),
            ResoniteTerrainGridGeometry heightMap => EstimateTerrainGridWorkingSetBytes(heightMap),
            ResoniteDynamicTerrainGeometry dynamicTerrain => checked(
                (EstimateTriangleMeshWorkingSetBytes(dynamicTerrain.StaticMesh.Mesh, cityObject.Materials) * TriangleMeshExpansionFactor)
                + EstimateTerrainGridWorkingSetBytes(dynamicTerrain.GridMesh)),
            _ => MinimumWeightBytes,
        };

        ResoniteTexturePayload[] distinctTexturePayloads = cityObject.Materials
            .Where(static material => material.TexturePayload is not null)
            .Select(static material => material.TexturePayload!)
            .Distinct(TexturePayloadReferenceComparer.Instance)
            .ToArray();
        long directTexturePayloadWeightBytes = distinctTexturePayloads.Sum(static payload => (long)payload.BinaryPayload.Length);
        long terrainOverlayWeightBytes = cityObject.Materials
            .Where(static material => material.TerrainOverlay is not null)
            .Select(static material => material.TerrainOverlay!)
            .Distinct()
            .Sum(EstimateTerrainOverlayWorkingSetBytes);
        long materialWeightBytes = checked(
            (cityObject.Materials.Count * MaterialBindingWeightBytes)
            + (distinctTexturePayloads.Length * TextureReferenceWeightBytes)
            + directTexturePayloadWeightBytes
            + terrainOverlayWeightBytes);
        return Math.Max(MinimumWeightBytes, geometryWeightBytes + materialWeightBytes);
    }

    private static long EstimateTerrainGridWorkingSetBytes(ResoniteTerrainGridGeometry heightMap)
    {
        return checked(
            (heightMap.HeightSamples.Count * HeightSampleWeightBytes)
            + (((long)heightMap.Width * heightMap.Height * HdrHeightTextureWeightBytes) * HeightMapExpansionFactor));
    }

    private static long EstimateTriangleMeshWorkingSetBytes(
        ResoniteImportedMesh mesh,
        IReadOnlyList<ResoniteMaterialBinding> materials)
    {
        bool requiresUvNormalization = materials.Any(ResoniteDynamicMaterialUvNormalizer.ShouldNormalizeTextureTransform);
        long normalizedVertexCount = requiresUvNormalization
            ? mesh.Submeshes.Sum(static submesh => (long)submesh.TriangleVertexIndices.Count)
            : mesh.Vertices.Count;
        long sourceVertexCount = mesh.Vertices.Count;
        long vertexBytes = requiresUvNormalization
            ? checked((sourceVertexCount + normalizedVertexCount) * VertexWeightBytes)
            : sourceVertexCount * VertexWeightBytes;
        long indexBytes = mesh.Submeshes.Sum(static submesh => (long)submesh.TriangleVertexIndices.Count * IndexWeightBytes);
        long submeshBytes = mesh.Submeshes.Count * PerSubmeshWeightBytes;
        return checked(vertexBytes + indexBytes + submeshBytes);
    }

    private static long EstimateTerrainOverlayWorkingSetBytes(TerrainTextureOverlay overlay)
    {
        const long rgbaBytesPerPixel = 4L;

        TerrainTextureTileSource? highestResolutionTileSource = overlay.EnumerateTileSources()
            .OrderByDescending(static source => source.ZoomLevel)
            .FirstOrDefault();
        if (highestResolutionTileSource is null)
        {
            return TextureReferenceWeightBytes;
        }

        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(
            overlay.GeographicBounds,
            highestResolutionTileSource.ZoomLevel);
        int maxTextureEdge = RoundDownToPowerOfTwo(overlay.MaxTextureSize);
        int estimatedWidth = Math.Min(RoundUpToPowerOfTwo(layout.CropWidth), maxTextureEdge);
        int estimatedHeight = Math.Min(RoundUpToPowerOfTwo(layout.CropHeight), maxTextureEdge);
        return Math.Max(TextureReferenceWeightBytes, checked((long)estimatedWidth * estimatedHeight * rgbaBytesPerPixel));
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        int rounded = 1;
        while (rounded < value)
        {
            rounded <<= 1;
        }

        return rounded;
    }

    private static int RoundDownToPowerOfTwo(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        int rounded = 1;
        while ((rounded << 1) > 0 && (rounded << 1) <= value)
        {
            rounded <<= 1;
        }

        return rounded;
    }

    private sealed class TexturePayloadReferenceComparer : IEqualityComparer<ResoniteTexturePayload>
    {
        internal static readonly TexturePayloadReferenceComparer Instance = new();

        public bool Equals(ResoniteTexturePayload? x, ResoniteTexturePayload? y) => ReferenceEquals(x, y);

        public int GetHashCode(ResoniteTexturePayload obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
