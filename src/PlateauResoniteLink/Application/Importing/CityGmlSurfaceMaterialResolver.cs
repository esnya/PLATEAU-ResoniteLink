using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlSurfaceMaterialResolver
{
    internal static readonly MaterialDepthOffset TerrainAlignedDepthOffset = new(-10.0, -10.0);

    private const double UnknownRoofBottomAltitudeToleranceMeters = 0.1;
    private static readonly ColorRgba DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly ColorRgba DefaultVegetationMaterialColor = new(0.32, 0.58, 0.24, 1.0);

    internal static ResolvedSurfaceMaterial[] ResolveSurfaces(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);
        ArgumentNullException.ThrowIfNull(materialResolver);

        HashSet<string> culledSurfaceIds = CityGmlSurfaceProjectionPolicy.GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        double cityObjectMinAltitude = CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices),
            static point => point.Altitude);

        return
        [
            .. cityObject.Surfaces
                .Where(surface => !culledSurfaceIds.Contains(surface.PolygonId))
                .Select(surface => ResolveSurfaceMaterial(
                    cityObject,
                    cityObjectOrigin,
                    cityObjectCartesian,
                    surface,
                    cityObjectMinAltitude,
                    demTerrainTextureOverlay,
                    materialResolver)),
        ];
    }

    internal static MaterialBinding[] CreateSharedCommonMaterialBindings(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        return ResolveSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver)
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) => CreateMaterialBinding(
                cityObject.ActualMeshCode,
                group.First(),
                materialIndex))
            .Where(static material => material.ReuseScope == MaterialReuseScope.Shared)
            .ToArray();
    }

    internal static MaterialBinding[] CreateDemTerrainGridMaterials(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshCodeBounds,
        IDefaultMaterialResolver materialResolver)
    {
        return ResolveSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver)
            .GroupBy(
                resolvedSurface => MaterialGroupingPolicy.CreateKey(
                    TerrainOverlayMeshCodeResolver.ResolveMaterialMeshCodeSource(
                        cityObject.ActualMeshCode,
                        requestedMeshCode,
                        requestedMeshCodeBounds,
                        resolvedSurface.Material.TerrainOverlay),
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                string terrainMaterialMeshCodeSource = TerrainOverlayMeshCodeResolver.ResolveMaterialMeshCodeSource(
                    cityObject.ActualMeshCode,
                    requestedMeshCode,
                    requestedMeshCodeBounds,
                    representativeSurface.Material.TerrainOverlay);
                return CreateMaterialBinding(
                    terrainMaterialMeshCodeSource,
                    representativeSurface,
                    materialIndex);
            })
            .ToArray();
    }

    internal static MaterialBinding CreateMaterialBinding(
        string actualMeshCode,
        ResolvedSurfaceMaterial representativeSurface,
        int materialIndex)
    {
        string? terrainMeshCode = representativeSurface.Material.TerrainOverlay is null
            ? null
            : TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, representativeSurface.Material.TerrainOverlay)
                ?? throw TerrainOverlayDiagnostics.CreateMeshCodeMismatchException(
                    "material-binding",
                    actualMeshCode,
                    actualMeshCode,
                    requestedMeshCodeBounds: null,
                    representativeSurface.Material.TerrainOverlay);
        ColorRgba baseColor = representativeSurface.Material.TerrainOverlay is null
            ? ToContractColor(representativeSurface.Surface.BaseColor)
            : new ColorRgba(1.0, 1.0, 1.0, 1.0);
        DefaultCommonMaterialMember? commonMaterial = DefaultCommonMaterialAssignment.Resolve(
            baseColor,
            representativeSurface.Material.MaterialType,
            representativeSurface.Material.TexturePayload,
            representativeSurface.Material.TextureSourceKind,
            representativeSurface.Material.Projection,
            representativeSurface.DepthOffset,
            representativeSurface.Material.TextureScale,
            representativeSurface.Material.TextureOffset,
            representativeSurface.Material.TerrainOverlay,
            representativeSurface.Material.CommonMaterial);
        return new MaterialBinding(
            BaseColor: baseColor,
            MaterialType: representativeSurface.Material.MaterialType,
            TexturePayload: representativeSurface.Material.TexturePayload,
            TextureSourceKind: representativeSurface.Material.TextureSourceKind,
            Projection: representativeSurface.Material.Projection,
            DepthOffset: representativeSurface.DepthOffset,
            SubmeshIndices: [materialIndex],
            TextureScale: representativeSurface.Material.TextureScale,
            Family: representativeSurface.Material.Family,
            TextureOffset: representativeSurface.Material.TextureOffset,
            ReuseScope: representativeSurface.Material.ReuseScope,
            TerrainOverlay: representativeSurface.Material.TerrainOverlay,
            BundledVariantIndex: representativeSurface.Material.BundledVariantIndex,
            TerrainMeshCode: terrainMeshCode,
            CommonMaterial: commonMaterial);
    }

    private static ResolvedSurfaceMaterial ResolveSurfaceMaterial(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ParsedSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
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
                    TerrainOverlay: demTerrainTextureOverlay),
                DepthOffset: null);
        }

        ResolvedMaterial? roofTerrainTextureMaterial = TryCreateRoofTerrainTextureMaterial(
            cityObject.ActualMeshCode,
            cityObject.PackageName,
            surface,
            cityObjectMinAltitude,
            demTerrainTextureOverlay,
            cityObjectOrigin,
            cityObjectCartesian);
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
                TerrainAlignedDepthOffset);
        }

        bool preferUvProjection = ShouldPreferUvProjection(
            cityObject.PackageName,
            surface,
            cityObjectOrigin,
            cityObjectCartesian);
        ResolvedMaterial resolvedMaterial = materialResolver.ResolveMaterial(new DefaultMaterialRequest(
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
                : BuildingAttributeQueries.TryGetKnownPositiveMetric(cityObject.BuildingAttributes.BuildingFootprintArea),
            SurfaceRole: ToDefaultMaterialSurfaceRole(surface.Semantic)));
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? TerrainAlignedDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(surface, resolvedMaterial, depthOffset);
    }

    private static ResolvedMaterial? TryCreateRoofTerrainTextureMaterial(
        string actualMeshCode,
        string packageName,
        ParsedSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (demTerrainTextureOverlay is null
            || TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, demTerrainTextureOverlay) is null
            || surface.TexturePayload is not null
            || !PlateauPackageCatalog.IsBuildingPackage(packageName)
            || !IsRoofTerrainTextureSurface(surface, cityObjectMinAltitude, cityObjectOrigin, cityObjectCartesian))
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
            TerrainOverlay: demTerrainTextureOverlay);
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
                && CityGmlSurfaceProjectionPolicy.IsNearHorizontalSurface(surface, cityObjectOrigin, cityObjectCartesian);
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

        return CityGmlSurfaceProjectionPolicy.IsFacadeSurface(surface, cityObjectOrigin, cityObjectCartesian);
    }

    private static DefaultMaterialSurfaceRole ToDefaultMaterialSurfaceRole(ParsedSurfaceSemantic semantic)
    {
        return semantic switch
        {
            ParsedSurfaceSemantic.Wall => DefaultMaterialSurfaceRole.Wall,
            ParsedSurfaceSemantic.Roof => DefaultMaterialSurfaceRole.Roof,
            ParsedSurfaceSemantic.Ground => DefaultMaterialSurfaceRole.Ground,
            ParsedSurfaceSemantic.Closure => DefaultMaterialSurfaceRole.Closure,
            ParsedSurfaceSemantic.OuterCeiling => DefaultMaterialSurfaceRole.OuterCeiling,
            ParsedSurfaceSemantic.OuterFloor => DefaultMaterialSurfaceRole.OuterFloor,
            _ => DefaultMaterialSurfaceRole.Unknown,
        };
    }

    private static bool IsRoofTerrainTextureSurface(
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

        Float3? normal = CityGmlSurfaceProjectionPolicy.ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null
            && Math.Abs(normal.Y) >= 0.98
            && surface.Vertices.Min(static vertex => vertex.Altitude) > cityObjectMinAltitude + UnknownRoofBottomAltitudeToleranceMeters;
    }

    private static bool IsGeneratedRoadMarkingSurface(ParsedSurface surface)
    {
        return surface.PolygonId.Contains("_generated_marking", StringComparison.Ordinal);
    }

    private static bool HasExplicitMaterialColor(ColorRgba color)
    {
        return color.A > 0.0
            && (Math.Abs(color.R - 1.0) > 1e-6
                || Math.Abs(color.G - 1.0) > 1e-6
                || Math.Abs(color.B - 1.0) > 1e-6
                || Math.Abs(color.A - 1.0) > 1e-6);
    }

    private static ColorRgba ToContractColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);
}
