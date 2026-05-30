using System;
using System.Collections.Generic;
using System.Globalization;
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
                AssertUploadedTriangleMeshAssetBatch(await geometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    dynamicTerrain.StaticMesh.MeshSource,
                    progressReporter,
                    cancellationToken)),
                AssertUploadedTerrainGridAssetBatch(await geometryAssetAssembler.PrepareTerrainGridAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    CreateTerrainGridAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    dynamicTerrain.GridMesh.Geometry,
                    dynamicTerrain.GridMesh.HeightTextureSource,
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvScale(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvOffset(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    progressReporter,
                    cancellationToken))),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
    }

    private static PlannedGeometryAsset CreatePlannedGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        UploadedGeometryAssetBatch uploadedGeometryBatch)
    {
        GeometryIdentity identity = new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"geometry-{cityObject.PackageName}-{cityObject.SlotKey}-{uploadedGeometryBatch.MeshAssetSlotName}"));

        return uploadedGeometryBatch switch
        {
            UploadedTriangleMeshAssetBatch triangleMesh => new PlannedTriangleMeshGeometryAsset(
                identity,
                triangleMesh.MeshAssetSlotName,
                triangleMesh.MeshUri),
            UploadedTerrainGridAssetBatch heightMap => new PlannedTerrainGridGeometryAsset(
                identity,
                heightMap.MeshAssetSlotName,
                heightMap.TerrainGridAssetSlotName,
                heightMap.Geometry,
                heightMap.HeightTextureUri,
                heightMap.UvScale,
                heightMap.UvOffset),
            _ => throw new InvalidOperationException(
                $"Unsupported uploaded geometry asset batch type '{uploadedGeometryBatch.GetType().Name}'."),
        };
    }

    private static PlannedDynamicTerrainGeometryAsset CreatePlannedDynamicTerrainGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        UploadedTriangleMeshAssetBatch staticMeshBatch,
        UploadedTerrainGridAssetBatch gridMeshBatch)
    {
        GeometryIdentity identity = new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"geometry-{cityObject.PackageName}-{cityObject.SlotKey}-{staticMeshBatch.MeshAssetSlotName}"));

        return new PlannedDynamicTerrainGeometryAsset(
            identity,
            staticMeshBatch.MeshAssetSlotName,
            staticMeshBatch.MeshUri,
            gridMeshBatch.TerrainGridAssetSlotName,
            gridMeshBatch.Geometry,
            gridMeshBatch.HeightTextureUri,
            gridMeshBatch.UvScale,
            gridMeshBatch.UvOffset);
    }

    private static UploadedTriangleMeshAssetBatch AssertUploadedTriangleMeshAssetBatch(
        UploadedGeometryAssetBatch uploadedGeometryBatch)
    {
        return uploadedGeometryBatch as UploadedTriangleMeshAssetBatch
            ?? throw new InvalidOperationException(
                $"Unsupported uploaded static terrain asset batch type '{uploadedGeometryBatch.GetType().Name}'.");
    }

    private static UploadedTerrainGridAssetBatch AssertUploadedTerrainGridAssetBatch(
        UploadedGeometryAssetBatch uploadedGeometryBatch)
    {
        return uploadedGeometryBatch as UploadedTerrainGridAssetBatch
            ?? throw new InvalidOperationException(
                $"Unsupported uploaded terrain grid asset batch type '{uploadedGeometryBatch.GetType().Name}'.");
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
