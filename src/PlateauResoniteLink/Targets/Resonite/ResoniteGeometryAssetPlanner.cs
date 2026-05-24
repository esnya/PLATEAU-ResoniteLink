using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                    triangleMesh.MeshImport,
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
                    heightMap.HeightTextureImport,
                    ResolveTerrainGridUvScale(cityObject, heightMap.Geometry, preparedTerrainTextureDataByOverlay),
                    ResolveTerrainGridUvOffset(cityObject, heightMap.Geometry, preparedTerrainTextureDataByOverlay),
                    progressReporter,
                    cancellationToken)),
            PreparedDynamicTerrainGeometry dynamicTerrain => CreatePlannedDynamicTerrainGeometryAsset(
                cityObject,
                AssertUploadedTriangleMeshAssetBatch(await geometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    dynamicTerrain.StaticMesh.MeshImport,
                    progressReporter,
                    cancellationToken)),
                AssertUploadedTerrainGridAssetBatch(await geometryAssetAssembler.PrepareTerrainGridAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    CreateTerrainGridAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    dynamicTerrain.GridMesh.Geometry,
                    dynamicTerrain.GridMesh.HeightTextureImport,
                    ResolveTerrainGridUvScale(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    ResolveTerrainGridUvOffset(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    progressReporter,
                    cancellationToken))),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
    }

    private static ResoniteFloat2? ResolveTerrainGridUvScale(
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

    private static ResoniteFloat2? ResolveTerrainGridUvOffset(
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
