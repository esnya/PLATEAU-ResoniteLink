using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlSurfaceMaterialResolver
{
    internal static readonly MaterialDepthOffset TerrainAlignedDepthOffset = new(-10.0, -10.0);

    private static readonly ColorRgba DefaultMaterialColor = new(1.0, 1.0, 1.0, 1.0);
    private static readonly ColorRgba DefaultDemGroundMaterialColor = new(181.0 / 255.0, 176.0 / 255.0, 166.0 / 255.0, 1.0);
    private static readonly ColorRgba DefaultVegetationMaterialColor = new(0.32, 0.58, 0.24, 1.0);

    internal static ResolvedSurfaceMaterial[] ResolveSurfaces(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        return EnumerateSurfaces(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                demTerrainTextureOverlay,
                materialResolver)
            .ToArray();
    }

    internal static IEnumerable<ResolvedSurfaceMaterial> EnumerateSurfaces(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(cityObjectOrigin);
        ArgumentNullException.ThrowIfNull(materialResolver);

        HashSet<string> culledSurfaceIds = PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName)
            ? CityGmlSurfaceProjectionPolicy.GetCulledSurfaceIdsBeforeProjection(
                cityObject.PackageName,
                cityObject.Surfaces,
                cityObjectOrigin,
                cityObjectCartesian)
            : [];
        double cityObjectMinAltitude = CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            cityObject.Faces.SelectMany(static face => face.Surface.Vertices),
            static point => point.Altitude);

        foreach (ConstructionFace face in cityObject.Faces.Where(face => !culledSurfaceIds.Contains(face.Surface.PolygonId)))
        {
            yield return ResolveSurfaceMaterial(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                face,
                cityObjectMinAltitude,
                demTerrainTextureOverlay,
                materialResolver);
        }
    }

    internal static MaterialBinding[] CreateSharedCommonMaterialBindings(
        ConstructionCityObjectDraft cityObject,
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
            .Where(static resolvedSurface => resolvedSurface.Material.TerrainOverlay is null)
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
        ConstructionCityObjectDraft cityObject,
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
                    resolvedSurface.Material.TerrainOverlay?.MeshCode.Value ?? cityObject.ActualMeshCode,
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial representativeSurface = group.First();
                string terrainMaterialMeshCodeSource =
                    representativeSurface.Material.TerrainOverlay?.MeshCode.Value ?? cityObject.ActualMeshCode;
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
        ThirdRegionalMeshCode? terrainMeshCode = representativeSurface.Material.TerrainOverlay?.MeshCode;
        TerrainOverlayMaterialBinding? terrainOverlayMaterial = representativeSurface.Material.TerrainOverlay is null
            ? null
            : new TerrainOverlayMaterialBinding(
                terrainMeshCode!.Value,
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
            TerrainOverlayMaterial: terrainOverlayMaterial,
            BundledVariantIndex: representativeSurface.Material.BundledVariantIndex,
            CommonMaterial: commonMaterial);
    }

    private static ResolvedSurfaceMaterial ResolveSurfaceMaterial(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ConstructionFace face,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedSurface surface = face.Surface;
        if (surface.UsesGeneratedDemTexture)
        {
            ConstructionFace resolvedFace = demTerrainTextureOverlay is null
                ? face with { Surface = surface with { BaseColor = DefaultDemGroundMaterialColor } }
                : face;
            return new ResolvedSurfaceMaterial(
                resolvedFace,
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
            face,
            cityObjectMinAltitude,
            demTerrainTextureOverlay,
            cityObjectOrigin,
            cityObjectCartesian);
        if (roofTerrainTextureMaterial is not null)
        {
            return new ResolvedSurfaceMaterial(
                face with { Surface = surface with { BaseColor = DefaultMaterialColor } },
                roofTerrainTextureMaterial,
                DepthOffset: null);
        }

        if (string.Equals(cityObject.PackageName, "veg", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null)
        {
            if (HasExplicitMaterialColor(surface.BaseColor))
            {
                return new ResolvedSurfaceMaterial(
                    face,
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
                face with { Surface = surface with { BaseColor = DefaultVegetationMaterialColor } },
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
                face,
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
            face,
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
            SurfaceRole: ToDefaultMaterialSurfaceRole(face.Role)));
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? TerrainAlignedDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(face, resolvedMaterial, depthOffset);
    }

    private static ResolvedMaterial? TryCreateRoofTerrainTextureMaterial(
        string actualMeshCode,
        string packageName,
        ConstructionFace face,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (demTerrainTextureOverlay is null
            || face.Surface.TexturePayload is not null
            || !PlateauPackageCatalog.IsBuildingPackage(packageName)
            || !RoofTerrainTextureSurfacePolicy.IsRoofTerrainTextureSurface(
                face,
                cityObjectMinAltitude,
                cityObjectOrigin,
                cityObjectCartesian))
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
        ConstructionFace face,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ParsedSurface surface = face.Surface;
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

        if (face.Role is ConstructionFaceRole.Wall)
        {
            return true;
        }

        if (face.Role is ConstructionFaceRole.Roof
            or ConstructionFaceRole.RoofSlab
            or ConstructionFaceRole.Ground
            or ConstructionFaceRole.OuterCeiling
            or ConstructionFaceRole.OuterFloor)
        {
            return false;
        }

        return CityGmlSurfaceProjectionPolicy.IsFacadeSurface(surface, cityObjectOrigin, cityObjectCartesian);
    }

    private static DefaultMaterialSurfaceRole ToDefaultMaterialSurfaceRole(ConstructionFaceRole role)
    {
        return role switch
        {
            ConstructionFaceRole.Wall => DefaultMaterialSurfaceRole.Wall,
            ConstructionFaceRole.Roof or ConstructionFaceRole.RoofSlab => DefaultMaterialSurfaceRole.Roof,
            ConstructionFaceRole.Ground => DefaultMaterialSurfaceRole.Ground,
            ConstructionFaceRole.Closure => DefaultMaterialSurfaceRole.Closure,
            ConstructionFaceRole.OuterCeiling => DefaultMaterialSurfaceRole.OuterCeiling,
            ConstructionFaceRole.OuterFloor => DefaultMaterialSurfaceRole.OuterFloor,
            _ => DefaultMaterialSurfaceRole.Unknown,
        };
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
