using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlTriangleMeshCityObjectProjection
{
    internal static ImportedCityObject Project(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        return ProjectTriangleMesh(
            cityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            materialResolver).CityObject;
    }

    internal static TriangleMeshProjectedCityObject ProjectTriangleMesh(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver,
        GeodeticPoint? objectOriginOverride = null)
    {
        GeodeticPoint cityObjectOrigin = objectOriginOverride ?? ResolveCityObjectOrigin(cityObject);

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
        DemUvProjection? demUvProjection = TryCreateDemUvProjection(demTerrainTextureOverlay);

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
            .OrderBy(static group => GetMinimumSurface(group), ParsedSurfaceStructuralComparer.Instance)
            .ToArray();
        FacadeUvProjectionContext? facadeUvProjectionContext = CityGmlSurfaceProjectionPolicy.TryCreateFacadeUvProjectionContext(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian);

        for (int materialIndex = 0; materialIndex < materialGroups.Length; materialIndex++)
        {
            IGrouping<MaterialGroupingKey, ResolvedSurfaceMaterial> materialGroup = materialGroups[materialIndex];
            List<int> indices = [];

            foreach (ResolvedSurfaceMaterial resolvedSurface in materialGroup
                         .OrderBy(static surface => surface.Surface, ParsedSurfaceStructuralComparer.Instance))
            {
                SurfaceMeshTessellation tessellation = CityGmlSurfaceMeshTessellator.Tessellate(
                    new SurfaceMeshTessellationRequest(
                        cityObject.PackageName,
                        resolvedSurface.Face,
                        resolvedSurface.Material,
                        cityObjectOrigin,
                        cityObjectCartesian,
                        facadeUvProjectionContext,
                        demUvProjection));
                int baseIndex = vertices.Count;
                vertices.AddRange(tessellation.Vertices);
                foreach (int index in tessellation.Indices)
                {
                    indices.Add(baseIndex + index);
                }
            }

            if (indices.Count == 0)
            {
                continue;
            }

            ResolvedSurfaceMaterial representativeSurface = materialGroup.First();
            submeshes.Add(new MeshSubmesh(materialIndex, indices));
            materials.Add(CityGmlSurfaceMaterialResolver.CreateMaterialBinding(cityObject.ActualMeshCode, representativeSurface, materialIndex));
        }

        TriangleMeshGeometry geometry = new(new ImportedMesh(vertices.ToArray(), submeshes.ToArray()));
        ImportedCityObject projectedCityObject = new(
            ObjectKey: cityObject.SlotKey,
            DisplayName: cityObject.DisplayName,
            PackageName: cityObject.PackageName,
            ActualMeshCode: cityObject.ActualMeshCode,
            LodLevel: cityObject.LodLevel,
            Transform: new Transform3D(ToContractFloat3(slotPosition)),
            Geometry: geometry,
            Materials: materials,
            SourceFileRelativePath: cityObject.SourceFileRelativePath,
            Landmark: IsBuildingLandmark(cityObject));
        return new TriangleMeshProjectedCityObject(projectedCityObject, geometry);
    }

    private static bool IsBuildingLandmark(ConstructionCityObjectDraft cityObject)
    {
        return IsBuildingPackage(cityObject.PackageName)
            && BuildingFacadeScale.Classify(
                cityObject.FloorsAboveGround,
                cityObject.MeasuredHeightMeters,
                cityObject.GeometryHeightMeters,
                cityObject.BuildingAttributes.BuildingFootprintArea?.Value).Landmark;
    }

    private static bool IsBuildingPackage(string packageName)
    {
        return string.Equals(packageName, "bldg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(packageName, "ubld", StringComparison.OrdinalIgnoreCase);
    }

    private static GeodeticPoint ResolveCityObjectOrigin(ConstructionCityObjectDraft cityObject)
    {
        return CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static DemUvProjection? TryCreateDemUvProjection(TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null)
        {
            return null;
        }

        JisRegionalMeshBounds meshCodeBounds = demTerrainTextureOverlay.MeshCode.Bounds;
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

    private static ParsedSurface GetMinimumSurface(IEnumerable<ResolvedSurfaceMaterial> surfaces)
    {
        ParsedSurface? minimum = null;
        foreach (ResolvedSurfaceMaterial surface in surfaces)
        {
            if (minimum is null
                || ParsedSurfaceStructuralComparer.Instance.Compare(surface.Surface, minimum) < 0)
            {
                minimum = surface.Surface;
            }
        }

        return minimum ?? throw new InvalidOperationException("Material groups must not be empty.");
    }
}

internal sealed record TriangleMeshProjectedCityObject(
    ImportedCityObject CityObject,
    TriangleMeshGeometry Geometry);
