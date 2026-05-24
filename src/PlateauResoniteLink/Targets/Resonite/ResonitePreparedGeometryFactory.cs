using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResonitePreparedGeometryFactory
{
    void ValidateForPreparation(ResoniteConstructionCityObject cityObject);

    Task<PreparedConstructionGeometry> CreateAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken);

    ResoniteConstructionCityObject ApplyTerrainTextureCanvasUv(
        ResoniteConstructionCityObject cityObject,
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay);

    PreparedConstructionGeometry RecreateStaticMeshIfNeeded(
        ResoniteConstructionCityObject cityObject,
        PreparedConstructionGeometry preparedGeometry);
}

internal sealed class ResonitePreparedGeometryFactory : IResonitePreparedGeometryFactory
{
    private const string DemPackageName = "dem";

    public void ValidateForPreparation(ResoniteConstructionCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (cityObject.Geometry is ResoniteTriangleMeshGeometry triangleGeometry)
        {
            try
            {
                ValidateTriangleMeshBindings(cityObject, triangleGeometry.Mesh);
            }
            catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
            {
                throw CreateMeshValidationException(cityObject, triangleGeometry.Mesh, exception);
            }
        }
        else if (cityObject.Geometry is ResoniteDynamicTerrainGeometry dynamicTerrain)
        {
            try
            {
                ValidateTriangleMeshBindings(cityObject, dynamicTerrain.StaticMesh.Mesh);
            }
            catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
            {
                throw CreateMeshValidationException(cityObject, dynamicTerrain.StaticMesh.Mesh, exception);
            }
        }
    }

    public Task<PreparedConstructionGeometry> CreateAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        return cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => Task.Run<PreparedConstructionGeometry>(
                () => PrepareTriangleMeshGeometry(cityObject, triangleMesh.Mesh),
                cancellationToken),
            ResoniteTerrainGridGeometry heightMap => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedTerrainGridGeometry(heightMap, PrepareTerrainGridDisplacementTexture(heightMap)),
                cancellationToken),
            ResoniteDynamicTerrainGeometry dynamicTerrain => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedDynamicTerrainGeometry(
                    PrepareTriangleMeshGeometry(cityObject, dynamicTerrain.StaticMesh.Mesh),
                    new PreparedTerrainGridGeometry(
                        dynamicTerrain.GridMesh,
                        PrepareTerrainGridDisplacementTexture(dynamicTerrain.GridMesh))),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    public ResoniteConstructionCityObject ApplyTerrainTextureCanvasUv(
        ResoniteConstructionCityObject cityObject,
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay)
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

        bool clampCanvasUv = IsDemPackage(cityObject.PackageName);
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

    public PreparedConstructionGeometry RecreateStaticMeshIfNeeded(
        ResoniteConstructionCityObject cityObject,
        PreparedConstructionGeometry preparedGeometry)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(preparedGeometry);

        if (cityObject.Geometry is ResoniteTriangleMeshGeometry resolvedTriangleMesh
            && preparedGeometry is PreparedTriangleMeshGeometry)
        {
            return PrepareTriangleMeshGeometry(cityObject, resolvedTriangleMesh.Mesh);
        }

        if (cityObject.Geometry is ResoniteDynamicTerrainGeometry resolvedDynamicTerrain
            && preparedGeometry is PreparedDynamicTerrainGeometry preparedDynamicTerrain)
        {
            return preparedDynamicTerrain with
            {
                StaticMesh = PrepareTriangleMeshGeometry(cityObject, resolvedDynamicTerrain.StaticMesh.Mesh),
            };
        }

        return preparedGeometry;
    }

    private static void ValidateTriangleMeshBindings(
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

    private static PreparedTriangleMeshGeometry PrepareTriangleMeshGeometry(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        try
        {
            return new PreparedTriangleMeshGeometry(ResoniteMeshImportFactory.Create(mesh));
        }
        catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
        {
            throw CreateMeshValidationException(cityObject, mesh, exception);
        }
    }

    private static ResoniteMeshValidationException CreateMeshValidationException(
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

    private static string CreateTriangleMeshDiagnosticSummary(
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

    private static ResoniteRawHdrTextureImport PrepareTerrainGridDisplacementTexture(ResoniteTerrainGridGeometry geometry)
    {
        float[] rawPixels = new float[geometry.Width * geometry.Height * 4];
        double heightRange = Math.Max(geometry.MaxHeight - geometry.MinHeight, 0.0);

        for (int y = 0; y < geometry.Height; y++)
        {
            for (int x = 0; x < geometry.Width; x++)
            {
                double heightSample = geometry.HeightSamples[(y * geometry.Width) + x];
                double normalizedHeight = heightRange <= 1e-9
                    ? 0.0
                    : Math.Clamp((heightSample - geometry.MinHeight) / heightRange, 0.0, 1.0);
                float heightValue = (float)(1.0 - normalizedHeight);
                int pixelIndex = (y * geometry.Width * 4) + (x * 4);
                // FrooxEngine.GridMesh uses `color.r + color.g + color.b / 3` for displacement.
                // Encode the inverted height into blue only (scaled by 3) so the effective sampled height stays 1x.
                rawPixels[pixelIndex] = 0.0f;
                rawPixels[pixelIndex + 1] = 0.0f;
                rawPixels[pixelIndex + 2] = heightValue * 3.0f;
                rawPixels[pixelIndex + 3] = 1.0f;
            }
        }

        byte[] rawBytes = new byte[rawPixels.Length * sizeof(float)];
        Buffer.BlockCopy(rawPixels, 0, rawBytes, 0, rawBytes.Length);
        return new ResoniteRawHdrTextureImport(geometry.Width, geometry.Height, rawBytes);
    }

    private static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, DemPackageName, StringComparison.OrdinalIgnoreCase);
    }
}
