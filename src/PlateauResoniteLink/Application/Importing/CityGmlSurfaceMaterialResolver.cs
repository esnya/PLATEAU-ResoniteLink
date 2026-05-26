using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

using ProjectionPoint = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.GeodeticPoint;
using ProjectionSurface = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurface;
using ProjectionSurfaceSemantic = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurfaceSemantic;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlSurfaceMaterialResolver
{
    internal static readonly MaterialDepthOffset TerrainAlignedDepthOffset = new(-10.0, -10.0);

    private const double BuildingBottomCullBandMeters = 0.1;
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

        ProjectionSurface[] projectionSurfaces = cityObject.Surfaces
            .Select(CityGmlProjectionModelAdapter.ToProjectionModel)
            .ToArray();
        HashSet<string> culledSurfaceIds = GetCulledSurfaceIdsBeforeProjection(
            cityObject.PackageName,
            projectionSurfaces,
            cityObjectOrigin.ToProjectionModel(),
            cityObjectCartesian);
        double cityObjectMinAltitude = CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            projectionSurfaces.SelectMany(static surface => surface.Vertices),
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
                    requestedMeshCode: null,
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
        ProjectionSurface projectionSurface = CityGmlProjectionModelAdapter.ToProjectionModel(surface);
        if (projectionSurface.UsesGeneratedDemTexture)
        {
            return new ResolvedSurfaceMaterial(
                projectionSurface,
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
            projectionSurface,
            cityObjectMinAltitude,
            demTerrainTextureOverlay,
            cityObjectOrigin.ToProjectionModel(),
            cityObjectCartesian);
        if (roofTerrainTextureMaterial is not null)
        {
            return new ResolvedSurfaceMaterial(
                projectionSurface with { BaseColor = DefaultMaterialColor },
                roofTerrainTextureMaterial,
                DepthOffset: null);
        }

        if (string.Equals(cityObject.PackageName, "veg", StringComparison.OrdinalIgnoreCase)
            && projectionSurface.TexturePayload is null)
        {
            if (HasExplicitMaterialColor(projectionSurface.BaseColor))
            {
                return new ResolvedSurfaceMaterial(
                    projectionSurface,
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
                projectionSurface with { BaseColor = DefaultVegetationMaterialColor },
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

        if (IsGeneratedRoadMarkingSurface(projectionSurface))
        {
            return new ResolvedSurfaceMaterial(
                projectionSurface,
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
            projectionSurface,
            cityObjectOrigin.ToProjectionModel(),
            cityObjectCartesian);
        ResolvedMaterial resolvedMaterial = materialResolver.ResolveMaterial(new DefaultMaterialRequest(
            cityObject.PackageName,
            projectionSurface.TexturePayload,
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
            SurfaceRole: ToDefaultMaterialSurfaceRole(projectionSurface.Semantic)));
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? TerrainAlignedDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(projectionSurface, resolvedMaterial, depthOffset);
    }

    private static ResolvedMaterial? TryCreateRoofTerrainTextureMaterial(
        string actualMeshCode,
        string packageName,
        ProjectionSurface surface,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        ProjectionPoint cityObjectOrigin,
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
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
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

        if (surface.Semantic is ProjectionSurfaceSemantic.Wall)
        {
            return true;
        }

        if (surface.Semantic is ProjectionSurfaceSemantic.Roof
            or ProjectionSurfaceSemantic.Ground
            or ProjectionSurfaceSemantic.OuterCeiling
            or ProjectionSurfaceSemantic.OuterFloor)
        {
            return false;
        }

        return IsFacadeSurface(surface, cityObjectOrigin, cityObjectCartesian);
    }

    private static DefaultMaterialSurfaceRole ToDefaultMaterialSurfaceRole(ProjectionSurfaceSemantic semantic)
    {
        return semantic switch
        {
            ProjectionSurfaceSemantic.Wall => DefaultMaterialSurfaceRole.Wall,
            ProjectionSurfaceSemantic.Roof => DefaultMaterialSurfaceRole.Roof,
            ProjectionSurfaceSemantic.Ground => DefaultMaterialSurfaceRole.Ground,
            ProjectionSurfaceSemantic.Closure => DefaultMaterialSurfaceRole.Closure,
            ProjectionSurfaceSemantic.OuterCeiling => DefaultMaterialSurfaceRole.OuterCeiling,
            ProjectionSurfaceSemantic.OuterFloor => DefaultMaterialSurfaceRole.OuterFloor,
            _ => DefaultMaterialSurfaceRole.Unknown,
        };
    }

    private static bool IsRoofTerrainTextureSurface(
        ProjectionSurface surface,
        double cityObjectMinAltitude,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (surface.Semantic == ProjectionSurfaceSemantic.Roof)
        {
            return true;
        }

        if (surface.Semantic is not (ProjectionSurfaceSemantic.Unknown
            or ProjectionSurfaceSemantic.Ground
            or ProjectionSurfaceSemantic.OuterCeiling
            or ProjectionSurfaceSemantic.OuterFloor))
        {
            return false;
        }

        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null
            && Math.Abs(normal.Y) >= 0.98
            && surface.Vertices.Min(static vertex => vertex.Altitude) > cityObjectMinAltitude + UnknownRoofBottomAltitudeToleranceMeters;
    }

    private static bool IsGeneratedRoadMarkingSurface(ProjectionSurface surface)
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

    private static HashSet<string> GetCulledSurfaceIdsBeforeProjection(
        string packageName,
        IEnumerable<ProjectionSurface> surfaces,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return [];
        }

        SurfaceProjectionInfo[] candidates = surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        double objectMinimumY = candidates.Min(static info => info.MinimumY!.Value);
        double objectMaximumY = candidates.Max(static info => info.MaximumY!.Value);

        return candidates
            .Where(static info => info.IsNearHorizontal)
            .Where(info => info.MaximumY!.Value <= objectMinimumY + BuildingBottomCullBandMeters)
            .Where(info => objectMaximumY > info.MaximumY!.Value + BuildingBottomCullBandMeters)
            .Select(static info => info.Surface.PolygonId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsFacadeSurface(
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian) is not Float3 normal)
        {
            return false;
        }

        return Math.Abs(normal.Y) < 0.45;
    }

    private static bool IsNearHorizontalSurface(
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3? normal = ComputeSurfaceNormal(surface, cityObjectOrigin, cityObjectCartesian);
        return normal is not null && Math.Abs(normal.Y) >= 0.98;
    }

    private static SurfaceProjectionInfo CreateSurfaceProjectionInfo(
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length == 0)
        {
            return new SurfaceProjectionInfo(surface, null, null, false);
        }

        Float3? normal = ComputePolygonNormal(positions);
        bool isNearHorizontal = normal is not null && Math.Abs(normal.Y) >= 0.98;

        return new SurfaceProjectionInfo(
            surface,
            positions.Min(static position => position.Y),
            positions.Max(static position => position.Y),
            isNearHorizontal);
    }

    private static Float3? ComputeSurfaceNormal(
        ProjectionSurface surface,
        ProjectionPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        return ComputePolygonNormal(positions);
    }

    private static Float3 CreateScenePosition(
        ProjectionPoint point,
        ProjectionPoint origin,
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

    private static Float3? ComputePolygonNormal(IEnumerable<Float3> positions)
    {
        Float3[] points = positions.ToArray();
        if (points.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;

        for (int index = 0; index < points.Length; index++)
        {
            Float3 current = points[index];
            Float3 next = points[(index + 1) % points.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (magnitude < 1e-8)
        {
            return null;
        }

        return new Float3(normalX / magnitude, normalY / magnitude, normalZ / magnitude);
    }

    private static ColorRgba ToContractColor(ColorRgba value) => new(value.R, value.G, value.B, value.A);

    private readonly record struct SurfaceProjectionInfo(
        ProjectionSurface Surface,
        double? MinimumY,
        double? MaximumY,
        bool IsNearHorizontal);
}
