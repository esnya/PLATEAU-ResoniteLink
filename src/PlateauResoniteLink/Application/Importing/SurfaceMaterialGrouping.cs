using System;
using System.Collections.Generic;
using System.Linq;


namespace PlateauResoniteLink.Application.Importing;

internal sealed record SurfaceMaterialGroup(
    int MaterialIndex,
    IReadOnlyList<ResolvedSurfaceMaterial> Surfaces,
    MaterialBinding Binding);

internal static class SurfaceMaterialGrouping
{
    public static SurfaceMaterialGroup[] Create(
        string actualMeshCode,
        IEnumerable<ResolvedSurfaceMaterial> resolvedSurfaces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actualMeshCode);
        ArgumentNullException.ThrowIfNull(resolvedSurfaces);

        return Create(
            resolvedSurfaces,
            surface => actualMeshCode,
            representative => actualMeshCode);
    }

    public static SurfaceMaterialGroup[] CreateForTerrain(
        string actualMeshCode,
        string requestedMeshCode,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        IEnumerable<ResolvedSurfaceMaterial> resolvedSurfaces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actualMeshCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedMeshCode);
        ArgumentNullException.ThrowIfNull(resolvedSurfaces);

        return Create(
            resolvedSurfaces,
            surface => TerrainTextureMeshCodeResolver.ResolveMaterialMeshCodeSource(
                actualMeshCode,
                requestedMeshCode,
                requestedMeshAreas,
                surface.Material.TerrainOverlay),
            representative => TerrainTextureMeshCodeResolver.ResolveMaterialMeshCodeSource(
                actualMeshCode,
                requestedMeshCode,
                requestedMeshAreas,
                representative.Material.TerrainOverlay));
    }

    private static SurfaceMaterialGroup[] Create(
        IEnumerable<ResolvedSurfaceMaterial> resolvedSurfaces,
        Func<ResolvedSurfaceMaterial, string> groupingMeshCodeSource,
        Func<ResolvedSurfaceMaterial, string> bindingMeshCodeSource)
    {
        return resolvedSurfaces
            .GroupBy(
                resolvedSurface => MaterialGroupingKey.Create(
                    groupingMeshCodeSource(resolvedSurface),
                    resolvedSurface.Material,
                    resolvedSurface.DepthOffset,
                    resolvedSurface.Material.TextureScale,
                    resolvedSurface.Surface.BaseColor,
                    resolvedSurface.Material.TextureOffset))
            .OrderBy(static group => group.Min(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface)), StringComparer.Ordinal)
            .Select((group, materialIndex) =>
            {
                ResolvedSurfaceMaterial[] orderedSurfaces = group
                    .OrderBy(static surface => ParsedSurfaceStableSortKey.Create(surface.Surface), StringComparer.Ordinal)
                    .ToArray();
                ResolvedSurfaceMaterial representativeSurface = group.First();
                return new SurfaceMaterialGroup(
                    materialIndex,
                    orderedSurfaces,
                    CreateMaterialBinding(
                        bindingMeshCodeSource(representativeSurface),
                        representativeSurface,
                        materialIndex));
            })
            .ToArray();
    }

    private static MaterialBinding CreateMaterialBinding(
        string actualMeshCode,
        ResolvedSurfaceMaterial representativeSurface,
        int materialIndex)
    {
        string? terrainMeshCode = representativeSurface.Material.TerrainOverlay is null
            ? null
                : TerrainTextureMeshCodeResolver.ResolveForOverlay(actualMeshCode, representativeSurface.Material.TerrainOverlay)
                ?? throw TerrainTextureMeshCodeResolver.CreateMismatchException(
                    "material-binding",
                    actualMeshCode,
                    actualMeshCode,
                    requestedMeshAreas: null,
                    representativeSurface.Material.TerrainOverlay);
        ColorRgba baseColor = representativeSurface.Material.TerrainOverlay is null
            ? new ColorRgba(
                representativeSurface.Surface.BaseColor.R,
                representativeSurface.Surface.BaseColor.G,
                representativeSurface.Surface.BaseColor.B,
                representativeSurface.Surface.BaseColor.A)
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
            TexturePayload: representativeSurface.Material.TexturePayload is null
                ? null
                : representativeSurface.Material.TexturePayload,
            TextureSourceKind: representativeSurface.Material.TextureSourceKind,
            Projection: representativeSurface.Material.Projection,
            DepthOffset: representativeSurface.DepthOffset is null
                ? null
                : representativeSurface.DepthOffset,
            SubmeshIndices: [materialIndex],
            TextureScale: representativeSurface.Material.TextureScale is null
                ? null
                : representativeSurface.Material.TextureScale,
            Family: representativeSurface.Material.Family,
            TextureOffset: representativeSurface.Material.TextureOffset is null
                ? null
                : representativeSurface.Material.TextureOffset,
            ReuseScope: representativeSurface.Material.ReuseScope,
            TerrainOverlay: representativeSurface.Material.TerrainOverlay,
            BundledVariantIndex: representativeSurface.Material.BundledVariantIndex,
            TerrainMeshCode: terrainMeshCode,
            CommonMaterial: commonMaterial);
    }
}
