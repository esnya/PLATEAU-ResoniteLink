using System;
using System.Globalization;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteTerrainTextureMaterialContract
{
    public static ResoniteMaterialBinding ValidateForEmission(
        ResoniteConstructionCityObject cityObject,
        int materialIndex,
        ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(material);

        if (material.TerrainOverlay is null)
        {
            return material with { TerrainMeshCode = null };
        }

        if (material.TerrainMeshCode is null)
        {
            throw CreateTerrainOverlayMaterialContractException(
                cityObject,
                materialIndex,
                material,
                "missing terrain mesh code");
        }

        return material with
        {
            TerrainMeshCode = ValidateMeshCode(
                cityObject,
                materialIndex,
                material,
                material.TerrainMeshCode,
                material.TerrainOverlay),
        };
    }

    public static string ValidateMeshCode(
        ResoniteConstructionCityObject cityObject,
        int materialIndex,
        ResoniteMaterialBinding material,
        string meshCode,
        TerrainTextureOverlay terrainOverlay)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);
        ArgumentNullException.ThrowIfNull(terrainOverlay);

        if (meshCode.Length == 8
            && PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds)
            && BoundsApproximatelyEqual(bounds, terrainOverlay.GeographicBounds))
        {
            return meshCode;
        }

        throw CreateTerrainOverlayMaterialContractException(
            cityObject,
            materialIndex,
            material,
            "mesh code bounds do not match overlay bounds");
    }

    private static InvalidOperationException CreateTerrainOverlayMaterialContractException(
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
                $"package='{overlay.PackageName}', bounds='{FormatGeographicBounds(overlay.GeographicBounds)}', sources='{overlay.SourceDescriptorKey}'");
        return new InvalidOperationException(
            "Terrain overlay material requires a third-level mesh code that matches the overlay geographic bounds. "
            + $"reason='{reason}', object_slot='{cityObject.SlotKey}', object_name='{cityObject.DisplayName}', "
            + $"package='{cityObject.PackageName}', actual_mesh_code='{cityObject.ActualMeshCode}', source_file='{cityObject.SourceFileRelativePath ?? "<null>"}', "
            + $"material_index='{materialIndex}', terrain_mesh='{material.TerrainMeshCode ?? "<null>"}', "
            + $"terrain_mesh_code='{material.TerrainMeshCode ?? "<null>"}', overlay={overlaySummary}.");
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
        (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds,
        GeographicRectangle geographicBounds)
    {
        const double tolerance = 1e-8;
        return Math.Abs(bounds.SouthLatitude - geographicBounds.MinLatitude) <= tolerance
            && Math.Abs(bounds.NorthLatitude - geographicBounds.MaxLatitude) <= tolerance
            && Math.Abs(bounds.WestLongitude - geographicBounds.MinLongitude) <= tolerance
            && Math.Abs(bounds.EastLongitude - geographicBounds.MaxLongitude) <= tolerance;
    }
}
