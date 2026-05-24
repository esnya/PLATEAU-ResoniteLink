using System;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record SurfaceMaterialResolutionRequest(
    ParsedCityObject CityObject,
    GeodeticPoint CityObjectOrigin,
    LocalCartesian? CityObjectCartesian,
    ParsedSurface Surface,
    double CityObjectMinAltitude,
    TerrainTextureOverlay? DemTerrainTextureOverlay,
    IDefaultMaterialResolver MaterialResolver);

internal static class SurfaceMaterialResolver
{
    private const double UnknownRoofBottomAltitudeToleranceMeters = 0.1;

    private static readonly ColorRgba DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly ColorRgba DefaultVegetationMaterialColor = new(0.32, 0.58, 0.24, 1.0);

    public static ResolvedSurfaceMaterial Resolve(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ParsedSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(materialResolver);

        return Resolve(new SurfaceMaterialResolutionRequest(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            surface,
            cityObjectMinAltitude,
            demTerrainTextureOverlay,
            materialResolver));
    }

    public static ResolvedSurfaceMaterial Resolve(SurfaceMaterialResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ParsedSurface surface = request.Surface;
        ParsedCityObject cityObject = request.CityObject;
        if (surface.UsesGeneratedDemTexture)
        {
            return new ResolvedSurfaceMaterial(
                surface,
                new ResolvedMaterial(
                    MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind.Dataset,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject,
                    TerrainOverlay: request.DemTerrainTextureOverlay),
                DepthOffset: null);
        }

        ResolvedMaterial? roofTerrainTextureMaterial = TryCreateRoofTerrainTextureMaterial(request);
        if (roofTerrainTextureMaterial is not null)
        {
            return new ResolvedSurfaceMaterial(
                surface with { BaseColor = DefaultMaterialColor },
                roofTerrainTextureMaterial,
                DepthOffset: null);
        }

        if (string.Equals(cityObject.PackageName, "veg", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null)
        {
            if (HasExplicitMaterialColor(surface.BaseColor))
            {
                return new ResolvedSurfaceMaterial(
                    surface,
                    new ResolvedMaterial(
                        MaterialType.VertexColor,
                        TexturePayload: null,
                        TextureSourceKind.Bundled,
                        MaterialProjection.Uv,
                        Family: null,
                        TextureScale: null,
                        ReuseScope: MaterialReuseScope.PerObject),
                    DepthOffset: null);
            }

            return new ResolvedSurfaceMaterial(
                surface with { BaseColor = DefaultVegetationMaterialColor },
                new ResolvedMaterial(
                    MaterialType.Standard,
                    TexturePayload: null,
                    TextureSourceKind.Bundled,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject),
                DepthOffset: null);
        }

        if (IsGeneratedRoadMarkingSurface(surface))
        {
            return new ResolvedSurfaceMaterial(
                surface,
                new ResolvedMaterial(
                    MaterialType.VertexColor,
                    TexturePayload: null,
                    TextureSourceKind.Bundled,
                    MaterialProjection.Uv,
                    Family: null,
                    TextureScale: null,
                    ReuseScope: MaterialReuseScope.PerObject),
                LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);
        }

        bool preferUvProjection = ShouldPreferUvProjection(
            cityObject.PackageName,
            surface,
            request.CityObjectOrigin,
            request.CityObjectCartesian);
        ResolvedMaterial resolvedMaterial = request.MaterialResolver.ResolveMaterial(new DefaultMaterialRequest(
            cityObject.PackageName,
            surface.TexturePayload,
            preferUvProjection,
            FamilyOverride: null,
            VariantSelectionKey: $"{cityObject.SlotKey}:{(preferUvProjection ? "uv" : "triplanar")}",
            BuildingAttributes: cityObject.BuildingAttributes,
            FloorsAboveGround: cityObject.FloorsAboveGround,
            MeasuredHeightMeters: cityObject.MeasuredHeightMeters,
            GeometryHeightMeters: cityObject.GeometryHeightMeters,
            FootprintAreaSquareMeters: cityObject.BuildingAttributes is null
                ? null
                : BuildingMetricNormalizer.TryGetKnownPositiveMetric(cityObject.BuildingAttributes.BuildingFootprintArea),
            SurfaceRole: DefaultMaterialSurfaceRoleMapper.From((ParsedSurfaceSemantic)surface.Semantic)));
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(surface, resolvedMaterial, depthOffset);
    }

    internal static bool IsRoofTerrainTextureSurface(
        ParsedSurface surface,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.Semantic == ParsedSurfaceSemantic.Roof)
        {
            return true;
        }

        if (surface.Semantic is not (ParsedSurfaceSemantic.Unknown
            or ParsedSurfaceSemantic.Ground
            or ParsedSurfaceSemantic.OuterCeiling
            or ParsedSurfaceSemantic.OuterFloor))
        {
            return false;
        }

        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null
            && Math.Abs(normal.Y) >= 0.98
            && IsAboveCityObjectBottomAltitude(surface, cityObjectMinAltitude);
    }

    private static ResolvedMaterial? TryCreateRoofTerrainTextureMaterial(SurfaceMaterialResolutionRequest request)
    {
        ParsedCityObject cityObject = request.CityObject;
        ParsedSurface surface = request.Surface;
        if (request.DemTerrainTextureOverlay is null
            || TerrainTextureMeshCodeResolver.ResolveForOverlay(cityObject.ActualMeshCode, request.DemTerrainTextureOverlay) is null
            || surface.TexturePayload is not null
            || !PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName)
            || !IsRoofTerrainTextureSurface(
                surface,
                request.CityObjectMinAltitude,
                request.CityObjectOrigin,
                request.CityObjectCartesian))
        {
            return null;
        }

        return new ResolvedMaterial(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Dataset,
            MaterialProjection.Uv,
            Family: null,
            TextureScale: null,
            ReuseScope: MaterialReuseScope.PerObject,
            TerrainOverlay: request.DemTerrainTextureOverlay);
    }

    private static bool ShouldPreferUvProjection(
        string packageName,
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.TexturePayload is not null)
        {
            return true;
        }

        if (string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return PlateauPackageCatalog.IsPathLikePackage(packageName)
                && IsNearHorizontalSurface(surface, cityObjectOrigin, cityObjectCartesian);
        }

        if (surface.Semantic is ParsedSurfaceSemantic.Wall)
        {
            return true;
        }

        if (surface.Semantic is ParsedSurfaceSemantic.Roof
            or ParsedSurfaceSemantic.Ground
            or ParsedSurfaceSemantic.OuterCeiling
            or ParsedSurfaceSemantic.OuterFloor)
        {
            return false;
        }

        return IsFacadeSurface(surface, cityObjectOrigin, cityObjectCartesian);
    }

    private static bool IsFacadeSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null && Math.Abs(normal.Y) < 0.45;
    }

    private static bool IsNearHorizontalSurface(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null && Math.Abs(normal.Y) >= 0.98;
    }

    private static Float3? ComputeSurfaceNormal(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => SceneAxisMapper.CreatePosition(
                point.Latitude,
                point.Longitude,
                point.Altitude,
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObjectCartesian))
            .ToArray();
        return SurfaceGeometryMath.ComputeNewellNormal(positions);
    }

    private static bool IsAboveCityObjectBottomAltitude(
        ParsedSurface surface,
        double cityObjectMinAltitude)
    {
        double surfaceMinAltitude = surface.Vertices.Min(static vertex => vertex.Altitude);
        return surfaceMinAltitude > cityObjectMinAltitude + UnknownRoofBottomAltitudeToleranceMeters;
    }

    private static bool IsGeneratedRoadMarkingSurface(ParsedSurface surface)
    {
        return surface.PolygonId.Contains("_generated_marking", StringComparison.Ordinal);
    }

    private static bool HasExplicitMaterialColor(ColorRgba color)
    {
        return Math.Abs(color.R - DefaultMaterialColor.R) >= 1e-8
            || Math.Abs(color.G - DefaultMaterialColor.G) >= 1e-8
            || Math.Abs(color.B - DefaultMaterialColor.B) >= 1e-8
            || Math.Abs(color.A - DefaultMaterialColor.A) >= 1e-8;
    }
}
