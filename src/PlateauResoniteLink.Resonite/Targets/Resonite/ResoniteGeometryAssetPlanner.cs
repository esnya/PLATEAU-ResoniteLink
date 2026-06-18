using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Resonite.Targets.Resonite.Execution;
using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResoniteGeometryAssetPlanner
{
    public static async Task<PlannedGeometryAsset> PlanAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(preparedCityObject);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        return preparedCityObject.Geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh => CreatePlannedGeometryAsset(
                await ResoniteGeometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    cityObject.DisplayName,
                    triangleMesh.MeshSource, cancellationToken)),
            PreparedTerrainGridGeometry heightMap => CreatePlannedGeometryAsset(
                await ResoniteGeometryAssetAssembler.PrepareTerrainGridAsync(
                    importClient,
                    cityObject.DisplayName,
                    heightMap.Geometry,
                    heightMap.HeightTextureSource,
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvScale(cityObject, heightMap.Geometry, preparedTerrainTextureDataByOverlay),
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvOffset(cityObject, heightMap.Geometry, preparedTerrainTextureDataByOverlay), cancellationToken)),
            PreparedDynamicTerrainGeometry dynamicTerrain => CreatePlannedDynamicTerrainGeometryAsset(
                await ResoniteGeometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    cityObject.DisplayName,
                    dynamicTerrain.StaticMesh.MeshSource, cancellationToken),
                await ResoniteGeometryAssetAssembler.PrepareTerrainGridAsync(
                    importClient,
                    cityObject.DisplayName,
                    dynamicTerrain.GridMesh.Geometry,
                    dynamicTerrain.GridMesh.HeightTextureSource,
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvScale(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvOffset(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay), cancellationToken)),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
    }

    private static PlannedTriangleMeshGeometryAsset CreatePlannedGeometryAsset(
        UploadedTriangleMeshAssetBatch uploadedGeometryBatch)
    {
        return new PlannedTriangleMeshGeometryAsset(uploadedGeometryBatch.MeshUri);
    }

    private static PlannedTerrainGridGeometryAsset CreatePlannedGeometryAsset(
        UploadedTerrainGridAssetBatch uploadedGeometryBatch)
    {
        return new PlannedTerrainGridGeometryAsset(
            uploadedGeometryBatch.Geometry,
            uploadedGeometryBatch.HeightTextureUri,
            uploadedGeometryBatch.UvScale,
            uploadedGeometryBatch.UvOffset);
    }

    private static PlannedDynamicTerrainGeometryAsset CreatePlannedDynamicTerrainGeometryAsset(
        UploadedTriangleMeshAssetBatch staticMeshBatch,
        UploadedTerrainGridAssetBatch gridMeshBatch)
    {
        return new PlannedDynamicTerrainGeometryAsset(
            staticMeshBatch.MeshUri,
            gridMeshBatch.Geometry,
            gridMeshBatch.HeightTextureUri,
            gridMeshBatch.UvScale,
            gridMeshBatch.UvOffset);
    }
}
