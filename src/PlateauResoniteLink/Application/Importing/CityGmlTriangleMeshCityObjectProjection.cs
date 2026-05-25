using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlTriangleMeshCityObjectProjection
{
    internal static ImportedCityObject Project(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        double? geometryHeightMeters = cityObject.GeometryHeightMeters
            ?? ResolveGeometryHeightMeters(cityObject.Surfaces);
        cityObject = GeneratedLod1RoofCityObjectFactory.Create(cityObject) with
        {
            GeometryHeightMeters = geometryHeightMeters,
        };
        GeodeticPoint cityObjectOrigin = ResolveCityObjectOrigin(cityObject);

        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        Float3 slotPosition = CreateScenePosition(
            cityObjectOrigin,
            globalOriginPoint,
            globalCartesian);
        List<MeshVertex> vertices = [];
        List<MeshSubmesh> submeshes = [];
        List<MaterialBinding> materials = [];
        DemUvProjection? demUvProjection = TryCreateDemUvProjection(cityObject.ActualMeshCode, demTerrainTextureOverlay);

        List<ResolvedSurfaceMaterial> resolvedSurfaces =
        [
            .. CityGmlSurfaceMaterialResolver.ResolveSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver),
        ];

        IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial>[] materialGroups = resolvedSurfaces
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .ToArray();

        for (int materialIndex = 0; materialIndex < materialGroups.Length; materialIndex++)
        {
            IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];
            FacadeUvProjectionContext? facadeUvProjectionContext = CityGmlSurfaceProjectionPolicy.TryCreateFacadeUvProjectionContext(
                cityObject.PackageName,
                cityObject.Surfaces,
                cityObjectOrigin,
                cityObjectCartesian);

            foreach (ResolvedSurfaceMaterial resolvedSurface in materialGroup
                         .OrderBy(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface), StringComparer.Ordinal))
            {
                SurfaceMeshTessellation tessellation = CityGmlSurfaceMeshTessellator.Tessellate(
                    new SurfaceMeshTessellationRequest(
                        cityObject.PackageName,
                        resolvedSurface.Surface,
                        resolvedSurface.Material,
                        cityObjectOrigin,
                        cityObjectCartesian,
                        globalOriginPoint,
                        globalCartesian,
                        facadeUvProjectionContext,
                        demUvProjection));
                int baseIndex = vertices.Count;
                vertices.AddRange(tessellation.Vertices);
                indices.AddRange(tessellation.Indices.Select(index => baseIndex + index));
            }

            if (indices.Count == 0)
            {
                continue;
            }

            ResolvedSurfaceMaterial representativeSurface = materialGroup.First();
            submeshes.Add(new MeshSubmesh(materialIndex, indices));
            materials.Add(CityGmlSurfaceMaterialResolver.CreateMaterialBinding(cityObject.ActualMeshCode, representativeSurface, materialIndex));
        }

        return new ImportedCityObject(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(ToContractFloat3(slotPosition)),
            Mesh: new ImportedMesh(vertices.ToArray(), submeshes.ToArray()),
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath);
    }

    private static GeodeticPoint ResolveCityObjectOrigin(ParsedCityObject cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static double? ResolveGeometryHeightMeters(IEnumerable<ParsedSurface> surfaces)
    {
        return CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters(
            surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static DemUvProjection? TryCreateDemUvProjection(
        string actualMeshCode,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, demTerrainTextureOverlay) is not { } terrainMeshCode
            || MeshCodeBounds.TryParse(terrainMeshCode) is not { } meshCodeBounds)
        {
            return null;
        }

        return CreateDemUvProjection(
            meshCodeBounds.WestLongitude,
            meshCodeBounds.EastLongitude,
            meshCodeBounds.NorthLatitude,
            meshCodeBounds.SouthLatitude);
    }

    private static DemUvProjection CreateDemUvProjection(
        double westLongitude,
        double eastLongitude,
        double northLatitude,
        double southLatitude)
    {
        double west = WebMercatorTileMath.LongitudeToNormalizedX(westLongitude);
        double east = WebMercatorTileMath.LongitudeToNormalizedX(eastLongitude);
        double north = WebMercatorTileMath.LatitudeToNormalizedY(northLatitude);
        double south = WebMercatorTileMath.LatitudeToNormalizedY(southLatitude);
        double width = Math.Max(east - west, 1e-12);
        double height = Math.Max(south - north, 1e-12);

        return new DemUvProjection(west, south, width, height);
    }

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
    {
        return SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            cartesian);
    }

    private static Float3 ToContractFloat3(Float3 value) => new(value.X, value.Y, value.Z);
}
