using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteGeometryAssetPlanner
{
    Task<PlannedGeometryAsset> PlanAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteGeometryAssetPlanner(
    IResoniteGeometryAssetAssembler geometryAssetAssembler) : IResoniteGeometryAssetPlanner
{
    private const string TerrainGridAssetSlotSuffix = "_terrain-grid";

    public async Task<PlannedGeometryAsset> PlanAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(preparedCityObject);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        return preparedCityObject.Geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh => CreatePlannedGeometryAsset(
                cityObject,
                await geometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    triangleMesh.MeshSource,
                    progressReporter,
                    cancellationToken)),
            PreparedTerrainGridGeometry heightMap => CreatePlannedGeometryAsset(
                cityObject,
                await geometryAssetAssembler.PrepareTerrainGridAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    CreateTerrainGridAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    heightMap.Geometry,
                    heightMap.HeightTextureSource,
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvScale(cityObject, heightMap.Geometry, preparedTerrainTextureDataByOverlay),
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvOffset(cityObject, heightMap.Geometry, preparedTerrainTextureDataByOverlay),
                    progressReporter,
                    cancellationToken)),
            PreparedDynamicTerrainGeometry dynamicTerrain => CreatePlannedDynamicTerrainGeometryAsset(
                cityObject,
                await geometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    dynamicTerrain.StaticMesh.MeshSource,
                    progressReporter,
                    cancellationToken),
                await geometryAssetAssembler.PrepareTerrainGridAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    CreateTerrainGridAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    dynamicTerrain.GridMesh.Geometry,
                    dynamicTerrain.GridMesh.HeightTextureSource,
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvScale(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvOffset(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    progressReporter,
                    cancellationToken)),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
    }

    private static PlannedTriangleMeshGeometryAsset CreatePlannedGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        UploadedTriangleMeshAssetBatch uploadedGeometryBatch)
    {
        return new PlannedTriangleMeshGeometryAsset(
            uploadedGeometryBatch.MeshAssetSlotName,
            uploadedGeometryBatch.MeshUri);
    }

    private static PlannedTerrainGridGeometryAsset CreatePlannedGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        UploadedTerrainGridAssetBatch uploadedGeometryBatch)
    {
        return new PlannedTerrainGridGeometryAsset(
            uploadedGeometryBatch.MeshAssetSlotName,
            uploadedGeometryBatch.TerrainGridAssetSlotName,
            uploadedGeometryBatch.Geometry,
            uploadedGeometryBatch.HeightTextureUri,
            uploadedGeometryBatch.UvScale,
            uploadedGeometryBatch.UvOffset);
    }

    private static PlannedDynamicTerrainGeometryAsset CreatePlannedDynamicTerrainGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        UploadedTriangleMeshAssetBatch staticMeshBatch,
        UploadedTerrainGridAssetBatch gridMeshBatch)
    {
        return new PlannedDynamicTerrainGeometryAsset(
            staticMeshBatch.MeshAssetSlotName,
            staticMeshBatch.MeshUri,
            gridMeshBatch.TerrainGridAssetSlotName,
            gridMeshBatch.Geometry,
            gridMeshBatch.HeightTextureUri,
            gridMeshBatch.UvScale,
            gridMeshBatch.UvOffset);
    }

    private static string CreateMeshAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return cityObject.DisplayName;
    }

    private static string CreateTerrainGridAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return string.Concat(CreateMeshAssetSlotName(cityObject), TerrainGridAssetSlotSuffix);
    }
}
