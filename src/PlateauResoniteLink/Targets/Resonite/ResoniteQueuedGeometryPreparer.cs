using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedGeometryPreparer
{
    ResoniteQueuedGeometryPreparation Start(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken);

    Task<PreparedQueuedGeometry> CompleteAsync(
        ResoniteQueuedGeometryPreparation preparation,
        IReadOnlyList<PreparedTextureReference> preparedTextures);
}

internal sealed class ResoniteQueuedGeometryPreparer : IResoniteQueuedGeometryPreparer
{
    public ResoniteQueuedGeometryPreparation Start(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        cityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);
        ValidateGeometry(cityObject);
        return new ResoniteQueuedGeometryPreparation(
            cityObject,
            CreateGeometryPreparationTask(cityObject, cancellationToken));
    }

    public async Task<PreparedQueuedGeometry> CompleteAsync(
        ResoniteQueuedGeometryPreparation preparation,
        IReadOnlyList<PreparedTextureReference> preparedTextures)
    {
        ArgumentNullException.ThrowIfNull(preparedTextures);

        PreparedConstructionGeometry preparedGeometry = await preparation.GeometryPreparationTask;
        ResoniteConstructionCityObject cityObject = ApplyTextureCanvasUv(
            preparation.CityObject,
            preparedTextures);
        preparedGeometry = RefreshPreparedGeometryAfterUvUpdate(
            cityObject,
            preparedGeometry);
        return new PreparedQueuedGeometry(cityObject, preparedGeometry);
    }

    private static void ValidateGeometry(ResoniteConstructionCityObject cityObject)
    {
        if (cityObject.Geometry is ResoniteTriangleMeshGeometry triangleGeometry)
        {
            ResoniteCityObjectPreparation.ValidateTriangleMeshBindingsForImport(cityObject, triangleGeometry.Mesh);
        }
        else if (cityObject.Geometry is ResoniteDynamicTerrainGeometry dynamicTerrain)
        {
            ResoniteCityObjectPreparation.ValidateTriangleMeshBindingsForImport(cityObject, dynamicTerrain.StaticMesh.Mesh);
        }
    }

    private static Task<PreparedConstructionGeometry> CreateGeometryPreparationTask(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        return cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => Task.Run<PreparedConstructionGeometry>(
                () => ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, triangleMesh.Mesh),
                cancellationToken),
            ResoniteTerrainGridGeometry heightMap => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedTerrainGridGeometry(heightMap, ResoniteCityObjectPreparation.PrepareTerrainGridDisplacementTexture(heightMap)),
                cancellationToken),
            ResoniteDynamicTerrainGeometry dynamicTerrain => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedDynamicTerrainGeometry(
                    ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, dynamicTerrain.StaticMesh.Mesh),
                    new PreparedTerrainGridGeometry(
                        dynamicTerrain.GridMesh,
                        ResoniteCityObjectPreparation.PrepareTerrainGridDisplacementTexture(dynamicTerrain.GridMesh))),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
    }

    private static ResoniteConstructionCityObject ApplyTextureCanvasUv(
        ResoniteConstructionCityObject cityObject,
        IReadOnlyList<PreparedTextureReference> preparedTextures)
    {
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay = preparedTextures
            .Where(static texture => texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            .ToDictionary(
                static texture => texture.TerrainOverlay!,
                static texture => texture.GeneratedTerrainTexture!);
        return ResoniteCityObjectPreparation.ApplyTerrainTextureCanvasUv(
            cityObject,
            preparedTerrainTextureDataByOverlay,
            clampCanvasUv: ResonitePackageSemantics.IsDemPackage(cityObject.PackageName));
    }

    private static PreparedConstructionGeometry RefreshPreparedGeometryAfterUvUpdate(
        ResoniteConstructionCityObject cityObject,
        PreparedConstructionGeometry preparedGeometry)
    {
        if (cityObject.Geometry is ResoniteTriangleMeshGeometry resolvedTriangleMesh
            && preparedGeometry is PreparedTriangleMeshGeometry)
        {
            return ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, resolvedTriangleMesh.Mesh);
        }

        if (cityObject.Geometry is ResoniteDynamicTerrainGeometry resolvedDynamicTerrain
            && preparedGeometry is PreparedDynamicTerrainGeometry preparedDynamicTerrain)
        {
            return preparedDynamicTerrain with
            {
                StaticMesh = ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, resolvedDynamicTerrain.StaticMesh.Mesh),
            };
        }

        return preparedGeometry;
    }
}

internal sealed record ResoniteQueuedGeometryPreparation(
    ResoniteConstructionCityObject CityObject,
    Task<PreparedConstructionGeometry> GeometryPreparationTask);

internal sealed record PreparedQueuedGeometry(
    ResoniteConstructionCityObject CityObject,
    PreparedConstructionGeometry Geometry);
