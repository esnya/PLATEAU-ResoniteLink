using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;
using PlateauResoniteLink.Plateau.Application.Importing.Source;

namespace PlateauResoniteLink.Plateau.Application.Importing.Plateau;

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

        HashSet<ParsedSurface> culledSurfaces = PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName)
            ? CityGmlSurfaceProjectionPolicy.GetCulledSurfacesBeforeProjection(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian)
            : new HashSet<ParsedSurface>(ReferenceEqualityComparer.Instance);
        double cityObjectMinAltitude = CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            cityObject.Faces.SelectMany(static face => face.Surface.Vertices),
            static point => point.Altitude);

        for (int faceIndex = 0; faceIndex < cityObject.Faces.Length; faceIndex++)
        {
            ConstructionFace face = cityObject.Faces[faceIndex];
            if (culledSurfaces.Contains(face.Surface))
            {
                continue;
            }

            yield return ResolveSurfaceMaterial(
                cityObject,
                cityObjectOrigin,
                cityObjectCartesian,
                face,
                faceIndex,
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
            .OrderBy(static group => GetMinimumSurface(group), ParsedSurfaceStructuralComparer.Instance)
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
            .OrderBy(static group => GetMinimumSurface(group), ParsedSurfaceStructuralComparer.Instance)
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
        return MaterialBinding.Create(
            baseColor,
            representativeSurface.Material.MaterialType,
            representativeSurface.Material.TexturePayload,
            representativeSurface.Material.TextureSourceKind,
            representativeSurface.Material.Projection,
            representativeSurface.DepthOffset,
            [materialIndex],
            representativeSurface.Material.TextureScale,
            representativeSurface.Material.Family,
            representativeSurface.Material.TextureOffset,
            representativeSurface.Material.ReuseScope,
            terrainOverlayMaterial,
            representativeSurface.Material.BundledVariantIndex,
            commonMaterial);
    }

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

        return minimum ?? throw new InvalidOperationException("Surface groups must not be empty.");
    }

    private static ResolvedSurfaceMaterial ResolveSurfaceMaterial(
        ConstructionCityObjectDraft cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        ConstructionFace face,
        int order,
        double cityObjectMinAltitude,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        IDefaultMaterialResolver materialResolver)
    {
        ParsedSurface surface = face.Surface;
        if (IsDemTerrainOverlaySurface(cityObject, surface))
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
                DepthOffset: null,
                order);
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
                DepthOffset: null,
                order);
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
                    DepthOffset: null,
                    order);
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
                DepthOffset: null,
                order);
        }

        if (face.MaterialTreatment == SurfaceMaterialTreatment.RoadMarking)
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
                TerrainAlignedDepthOffset,
                order);
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
            FootprintAreaSquareMeters: BuildingAttributeQueries.TryGetKnownPositiveMetric(cityObject.BuildingAttributes.BuildingFootprintArea),
            SurfaceRole: ToDefaultMaterialSurfaceRole(face.Role)));
        MaterialDepthOffset? depthOffset = cityObject.TerrainAligned
            ? TerrainAlignedDepthOffset
            : null;
        return new ResolvedSurfaceMaterial(face, resolvedMaterial, depthOffset, order);
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
            || !CanUseRoofTerrainTextureMaterial(
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

    private static bool CanUseRoofTerrainTextureMaterial(
        ConstructionFace face,
        double cityObjectMinAltitude,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        return face.MaterialTreatment is SurfaceMaterialTreatment.TerrainOverlayMaterialSource
            || RoofTerrainTextureSurfacePolicy.IsRoofTerrainTextureSurface(
                face,
                cityObjectMinAltitude,
                cityObjectOrigin,
                cityObjectCartesian);
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

    private static bool IsDemTerrainOverlaySurface(
        ConstructionCityObjectDraft cityObject,
        ParsedSurface surface)
    {
        return string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            && surface.TexturePayload is null;
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
