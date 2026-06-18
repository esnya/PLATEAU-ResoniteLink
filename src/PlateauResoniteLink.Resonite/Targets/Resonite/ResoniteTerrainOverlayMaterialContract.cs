using System;
using System.Globalization;

using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal static class ResoniteTerrainOverlayMaterialContract
{
    public static ResoniteMaterialBinding ValidateMaterial(
        ResoniteConstructionCityObject cityObject,
        int materialIndex,
        ResoniteMaterialBinding material)
    {
        if (material.TerrainOverlay is null)
        {
            return material with { TerrainOverlayMaterial = null };
        }

        _ = ValidateMeshCode(
            cityObject,
            materialIndex,
            material,
            material.TerrainOverlayMaterial!.MeshCode,
            material.TerrainOverlay);
        return material;
    }

    public static ThirdRegionalMeshCode ValidateMeshCode(
        ResoniteConstructionCityObject cityObject,
        int materialIndex,
        ResoniteMaterialBinding material,
        ThirdRegionalMeshCode meshCode,
        TerrainTextureOverlay terrainOverlay)
    {
        if (BoundsApproximatelyEqual(meshCode.Bounds, terrainOverlay.GeographicBounds))
        {
            return meshCode;
        }

        throw CreateException(
            cityObject,
            materialIndex,
            material,
            "mesh-code bounds do not match overlay bounds");
    }

    private static InvalidOperationException CreateException(
        ResoniteConstructionCityObject cityObject,
        int materialIndex,
        ResoniteMaterialBinding material,
        string reason)
    {
        TerrainTextureOverlay? overlay = material.TerrainOverlay;
        string overlaySummary = overlay is null
            ? "<null>"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"package='{overlay.PackageName}', bounds='{FormatGeographicBounds(overlay.GeographicBounds)}', sources='{overlay.SourceDescription}'");
        return new InvalidOperationException(
            "Terrain overlay material requires a third-level mesh-code that matches the overlay geographic bounds. "
            + $"reason='{reason}', object_slot='{cityObject.SlotKey}', object_name='{cityObject.DisplayName}', "
            + $"package='{cityObject.PackageName}', actual_mesh_code='{cityObject.ActualMeshCode}', source_file='{cityObject.SourceFileRelativePath ?? "<null>"}', "
            + $"material_index='{materialIndex}', terrain_mesh='{material.TerrainMeshCode ?? "<null>"}', overlay={overlaySummary}.");
    }

    private static string FormatGeographicBounds(GeographicRectangle bounds)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(bounds.MinLatitude)}-{FormatRounded(bounds.MaxLatitude)}-{FormatRounded(bounds.MinLongitude)}-{FormatRounded(bounds.MaxLongitude)}");
    }

    private static string FormatRounded(double value)
    {
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static bool BoundsApproximatelyEqual(
        JisRegionalMeshBounds bounds,
        GeographicRectangle geographicBounds)
    {
        const double tolerance = 1e-8;
        return Math.Abs(bounds.SouthLatitude - geographicBounds.MinLatitude) <= tolerance
            && Math.Abs(bounds.NorthLatitude - geographicBounds.MaxLatitude) <= tolerance
            && Math.Abs(bounds.WestLongitude - geographicBounds.MinLongitude) <= tolerance
            && Math.Abs(bounds.EastLongitude - geographicBounds.MaxLongitude) <= tolerance;
    }
}
