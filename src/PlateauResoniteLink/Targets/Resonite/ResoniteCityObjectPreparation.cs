using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteCityObjectPreparation
{
    public static void ValidateTriangleMeshBindings(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        if (mesh.Submeshes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' did not contain any submesh.");
        }

        if (cityObject.Materials.Count == 0)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' did not contain any material.");
        }

        Dictionary<int, ResoniteMeshSubmesh> submeshByIndex = mesh.Submeshes.ToDictionary(
            static submesh => submesh.Index,
            static submesh => submesh);
        if (submeshByIndex.Count != mesh.Submeshes.Count)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' contained duplicate submesh indices.");
        }

        Dictionary<int, int> materialOrdinalBySubmeshIndex = new();
        for (int materialOrdinal = 0; materialOrdinal < cityObject.Materials.Count; materialOrdinal++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialOrdinal];
            if (material.SubmeshIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Triangle mesh '{cityObject.DisplayName}' material #{materialOrdinal} did not target any submesh.");
            }

            foreach (int submeshIndex in material.SubmeshIndices)
            {
                if (!submeshByIndex.ContainsKey(submeshIndex))
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh '{cityObject.DisplayName}' material #{materialOrdinal} targeted missing submesh index {submeshIndex}.");
                }

                if (materialOrdinalBySubmeshIndex.TryGetValue(submeshIndex, out int existingMaterialOrdinal))
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh '{cityObject.DisplayName}' assigned submesh index {submeshIndex} to both material #{existingMaterialOrdinal} and material #{materialOrdinal}.");
                }

                materialOrdinalBySubmeshIndex[submeshIndex] = materialOrdinal;
            }
        }

        foreach (int submeshIndex in submeshByIndex.Keys.OrderBy(static index => index))
        {
            if (!materialOrdinalBySubmeshIndex.ContainsKey(submeshIndex))
            {
                throw new InvalidOperationException(
                    $"Triangle mesh '{cityObject.DisplayName}' left submesh index {submeshIndex} without a material assignment.");
            }
        }
    }

    public static void ValidateTriangleMeshBindingsForImport(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        try
        {
            ValidateTriangleMeshBindings(cityObject, mesh);
        }
        catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
        {
            throw CreateTriangleMeshValidationException(cityObject, mesh, exception);
        }
    }

    public static PreparedTriangleMeshGeometry PrepareTriangleMeshGeometry(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        try
        {
            return new PreparedTriangleMeshGeometry(ResoniteMeshImportFactory.Create(mesh));
        }
        catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
        {
            throw CreateTriangleMeshValidationException(cityObject, mesh, exception);
        }
    }

    public static ResoniteConstructionCityObject ApplyTerrainTextureCanvasUv(
        ResoniteConstructionCityObject cityObject,
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        bool clampCanvasUv)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        ResoniteTriangleMeshGeometry? triangleMesh = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry value => value,
            ResoniteDynamicTerrainGeometry value => value.StaticMesh,
            _ => null,
        };
        if (triangleMesh is null)
        {
            return cityObject;
        }

        Dictionary<int, GeneratedTerrainTexture> generatedTextureBySubmeshIndex = cityObject.Materials
            .Where(static material => material.TerrainOverlay is not null)
            .SelectMany(material =>
                material.TerrainOverlay is not null
                && preparedTerrainTextureDataByOverlay.TryGetValue(material.TerrainOverlay, out GeneratedTerrainTexture? generatedTexture)
                && !generatedTexture.OccupiedUvRect.IsIdentity
                    ? material.SubmeshIndices.Select(submeshIndex => KeyValuePair.Create(submeshIndex, generatedTexture))
                    : [])
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
        if (generatedTextureBySubmeshIndex.Count == 0)
        {
            return cityObject;
        }

        List<ResoniteMeshVertex> adjustedVertices = [];
        List<ResoniteMeshSubmesh> adjustedSubmeshes = [];
        foreach (ResoniteMeshSubmesh submesh in triangleMesh.Mesh.Submeshes)
        {
            List<int> adjustedIndices = new(submesh.TriangleVertexIndices.Count);
            foreach (int sourceIndex in submesh.TriangleVertexIndices)
            {
                ResoniteMeshVertex sourceVertex = triangleMesh.Mesh.Vertices[sourceIndex];
                ResoniteFloat2 adjustedUv = generatedTextureBySubmeshIndex.TryGetValue(submesh.Index, out GeneratedTerrainTexture? generatedTexture)
                    ? CreateCanvasAdjustedTerrainUv(sourceVertex.UV0, generatedTexture.OccupiedUvRect, clampCanvasUv)
                    : sourceVertex.UV0;
                adjustedVertices.Add(sourceVertex with { UV0 = adjustedUv });
                adjustedIndices.Add(adjustedVertices.Count - 1);
            }

            adjustedSubmeshes.Add(submesh with { TriangleVertexIndices = adjustedIndices });
        }

        ResoniteMaterialBinding[] adjustedMaterials = cityObject.Materials
            .Select(material =>
                material.TerrainOverlay is not null
                && preparedTerrainTextureDataByOverlay.TryGetValue(material.TerrainOverlay, out GeneratedTerrainTexture? generatedTexture)
                && !generatedTexture.OccupiedUvRect.IsIdentity
                    ? material with
                    {
                        TextureScale = null,
                        TextureOffset = null,
                    }
                    : material)
            .ToArray();

        ResoniteTriangleMeshGeometry adjustedTriangleGeometry = new(
            new ResoniteImportedMesh(adjustedVertices, adjustedSubmeshes));
        ResoniteConstructionGeometry adjustedGeometry = cityObject.Geometry is ResoniteDynamicTerrainGeometry dynamicTerrain
            ? dynamicTerrain with { StaticMesh = adjustedTriangleGeometry }
            : adjustedTriangleGeometry;

        return cityObject with
        {
            Geometry = adjustedGeometry,
            Materials = adjustedMaterials,
        };
    }

    public static ResoniteFloat2? ResolveTerrainGridUvScale(
        ResoniteConstructionCityObject cityObject,
        ResoniteTerrainGridGeometry geometry,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay)
    {
        TextureUvRect? terrainTextureRect = ResolveTerrainGridTerrainTextureRect(
            cityObject,
            geometry,
            preparedTerrainTextureDataByOverlay);
        return terrainTextureRect is null
            ? null
            : new ResoniteFloat2(terrainTextureRect.Value.ScaleValue.X, terrainTextureRect.Value.ScaleValue.Y);
    }

    public static ResoniteFloat2? ResolveTerrainGridUvOffset(
        ResoniteConstructionCityObject cityObject,
        ResoniteTerrainGridGeometry geometry,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay)
    {
        TextureUvRect? terrainTextureRect = ResolveTerrainGridTerrainTextureRect(
            cityObject,
            geometry,
            preparedTerrainTextureDataByOverlay);
        return terrainTextureRect is null
            ? null
            : new ResoniteFloat2(terrainTextureRect.Value.OffsetValue.X, terrainTextureRect.Value.OffsetValue.Y);
    }

    public static ITextureImportSource PrepareTerrainGridDisplacementTexture(ResoniteTerrainGridGeometry geometry)
    {
        return TextureImportSourceFactory.CreateGeneratedImage(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(CreateTerrainGridDisplacementPayload(geometry));
            },
            $"terrain-grid-height:{RuntimeHelpers.GetHashCode(geometry)}:{geometry.Width}x{geometry.Height}",
            $"terrain-grid-height:{geometry.Width}x{geometry.Height}",
            colorProfile: null,
            estimatedByteLength: checked((long)geometry.Width * geometry.Height * 4L * sizeof(float)));
    }

    private static RawTexturePayload CreateTerrainGridDisplacementPayload(ResoniteTerrainGridGeometry geometry)
    {
        float[] rawPixels = new float[geometry.Width * geometry.Height * 4];
        double heightRange = Math.Max(geometry.MaxHeight - geometry.MinHeight, 0.0);

        for (int y = 0; y < geometry.Height; y++)
        {
            for (int x = 0; x < geometry.Width; x++)
            {
                // FrooxEngine.GridMesh uses `color.r + color.g + color.b / 3` for displacement.
                // Encode the inverted height into blue only (scaled by 3) so the effective sampled height stays 1x.
                double heightSample = geometry.HeightSamples[(y * geometry.Width) + x];
                double normalizedHeight = heightRange <= 1e-9
                    ? 0.0
                    : Math.Clamp((heightSample - geometry.MinHeight) / heightRange, 0.0, 1.0);
                float heightValue = (float)(1.0 - normalizedHeight);
                int pixelIndex = (y * geometry.Width * 4) + (x * 4);
                rawPixels[pixelIndex] = 0.0f;
                rawPixels[pixelIndex + 1] = 0.0f;
                rawPixels[pixelIndex + 2] = heightValue * 3.0f;
                rawPixels[pixelIndex + 3] = 1.0f;
            }
        }

        byte[] rawBytes = new byte[rawPixels.Length * sizeof(float)];
        Buffer.BlockCopy(rawPixels, 0, rawBytes, 0, rawBytes.Length);
        return new RawTexturePayload(
            geometry.Width,
            geometry.Height,
            ColorProfile: null,
            rawBytes,
            RawTexturePayloadFormat.RgbaFloat32);
    }

    public static string CreateTriangleMeshDiagnosticSummary(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        int[] submeshIndices = mesh.Submeshes
            .Select(static submesh => submesh.Index)
            .OrderBy(static index => index)
            .ToArray();
        string materialSummary = string.Join(
            ", ",
            cityObject.Materials.Select(static (material, index) =>
                $"material#{index}[{string.Join("/", material.SubmeshIndices.OrderBy(static submeshIndex => submeshIndex))}]"));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"mesh_code={cityObject.ActualMeshCode}, vertices={mesh.Vertices.Count}, submeshes={mesh.Submeshes.Count}, "
            + $"submesh_indices=[{string.Join(", ", submeshIndices)}], materials={cityObject.Materials.Count}, "
            + $"material_bindings=[{materialSummary}]");
    }

    private static ResoniteMeshValidationException CreateTriangleMeshValidationException(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh,
        Exception exception)
    {
        return new ResoniteMeshValidationException(
            $"Triangle mesh '{cityObject.DisplayName}' failed sender-side validation. "
            + $"{CreateTriangleMeshDiagnosticSummary(cityObject, mesh)} "
            + $"Reason: {exception.Message}",
            exception);
    }

    private static ResoniteFloat2 CreateResoniteFloat2(ScalarPair value) => new(value.X, value.Y);

    private static ResoniteFloat2 CreateCanvasAdjustedTerrainUv(
        ResoniteFloat2 sourceUv,
        TextureUvRect occupiedUvRect,
        bool clampCanvasUv)
    {
        if (clampCanvasUv)
        {
            return CreateResoniteFloat2(TextureUvRect.RemapValue(
                new ScalarPair(sourceUv.X, sourceUv.Y),
                TextureUvRect.Identity,
                occupiedUvRect));
        }

        return new ResoniteFloat2(
            occupiedUvRect.MinU + (sourceUv.X * occupiedUvRect.Width),
            occupiedUvRect.MinV + (sourceUv.Y * occupiedUvRect.Height));
    }

    private static TextureUvRect? ResolveTerrainGridTerrainTextureRect(
        ResoniteConstructionCityObject cityObject,
        ResoniteTerrainGridGeometry geometry,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay)
    {
        TextureUvRect objectRect = geometry.UvScale is not null || geometry.UvOffset is not null
            ? TextureUvRect.FromScaleOffsetValue(
                geometry.UvScale is null ? new ScalarPair(1.0, 1.0) : new ScalarPair(geometry.UvScale.X, geometry.UvScale.Y),
                geometry.UvOffset is null ? new ScalarPair(0.0, 0.0) : new ScalarPair(geometry.UvOffset.X, geometry.UvOffset.Y))
            : TextureUvRect.Identity;

        TerrainTextureOverlay? overlay = cityObject.Materials
            .Select(static material => material.TerrainOverlay)
            .FirstOrDefault(static value => value is not null);
        if (overlay is null
            || !preparedTerrainTextureDataByOverlay.TryGetValue(overlay, out GeneratedTerrainTexture? generatedTerrainTexture))
        {
            return objectRect.IsIdentity ? null : objectRect;
        }

        return new TextureUvRect(
            generatedTerrainTexture.OccupiedUvRect.MinU + (objectRect.MinU * generatedTerrainTexture.OccupiedUvRect.Width),
            generatedTerrainTexture.OccupiedUvRect.MinV + (objectRect.MinV * generatedTerrainTexture.OccupiedUvRect.Height),
            objectRect.Width * generatedTerrainTexture.OccupiedUvRect.Width,
            objectRect.Height * generatedTerrainTexture.OccupiedUvRect.Height);
    }
}

internal abstract record PreparedConstructionGeometry;

internal sealed record PreparedTriangleMeshGeometry(
    IGeometryImportSource MeshSource)
    : PreparedConstructionGeometry;

internal sealed record PreparedTerrainGridGeometry(
    ResoniteTerrainGridGeometry Geometry,
    ITextureImportSource HeightTextureSource)
    : PreparedConstructionGeometry;

internal sealed record PreparedDynamicTerrainGeometry(
    PreparedTriangleMeshGeometry StaticMesh,
    PreparedTerrainGridGeometry GridMesh)
    : PreparedConstructionGeometry;

internal sealed record PreparedCityObject(
    ResoniteConstructionCityObject CityObject,
    PreparedConstructionGeometry Geometry,
    IReadOnlyList<PreparedTextureReference> Textures);

internal sealed record PreparedTextureReference(
    ResoniteTexturePayload? TexturePayload,
    ResoniteTextureSourceKind TextureSourceKind,
    ITextureImportSource TextureSource,
    string? TerrainMeshCode = null,
    TerrainTextureOverlay? TerrainOverlay = null,
    GeneratedTerrainTexture? GeneratedTerrainTexture = null);
