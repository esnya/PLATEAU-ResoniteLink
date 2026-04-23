using System;

namespace PlateauResoniteLink.Domain.Importing;

public static class FacadeMaterialUvScaling
{
    public const double FloorSquareMeters = 3.25;
    private const double NormalizedFacadeColumnsPerTexture = 6.0;
    private const double NormalizedFacadeRowsPerTexture = 6.0;

    public static readonly ScalarPair CommonMaterialScaleValue = CreateTilesPerMeterValue(
        NormalizedFacadeColumnsPerTexture,
        NormalizedFacadeRowsPerTexture);

    public static readonly BundledDefaultMaterialProfile Facade001Profile = CreateProfile(
        columnsPerTexture: 16.0,
        rowsPerTexture: 10.0);

    public static readonly BundledDefaultMaterialProfile Facade018AProfile = CreateProfile(
        columnsPerTexture: NormalizedFacadeColumnsPerTexture,
        rowsPerTexture: NormalizedFacadeRowsPerTexture);

    public static readonly BundledDefaultMaterialProfile Facade019AProfile = CreateProfile(
        columnsPerTexture: NormalizedFacadeColumnsPerTexture,
        rowsPerTexture: NormalizedFacadeRowsPerTexture);

    public static readonly BundledDefaultMaterialProfile Facade020AProfile = CreateProfile(
        columnsPerTexture: NormalizedFacadeColumnsPerTexture,
        rowsPerTexture: NormalizedFacadeRowsPerTexture);

    public static double ToFloorSquareUnits(double meters)
    {
        return meters / FloorSquareMeters;
    }

    public static BundledDefaultMaterialProfile GetBundledProfile(string texturePath)
    {
        return texturePath.ToLowerInvariant() switch
        {
            "default-materials/facade/facade001_2k-jpg_color.jpg" => Facade001Profile,
            "default-materials/facade/facade018a_2k-jpg_color.jpg" => Facade018AProfile,
            "default-materials/facade/facade019a_2k-jpg_color.jpg" => Facade019AProfile,
            "default-materials/facade/facade020a_2k-jpg_color.jpg" => Facade020AProfile,
            _ => new BundledDefaultMaterialProfile(CommonMaterialScaleValue),
        };
    }

    private static BundledDefaultMaterialProfile CreateProfile(
        double columnsPerTexture,
        double rowsPerTexture,
        double offsetColumns = 0.0,
        double offsetRows = 0.0)
    {
        return new BundledDefaultMaterialProfile(
            CreateTilesPerMeterValue(columnsPerTexture, rowsPerTexture),
            CreateTextureOffsetValue(columnsPerTexture, rowsPerTexture, offsetColumns, offsetRows));
    }

    private static ScalarPair CreateTilesPerMeterValue(double columnsPerTexture, double rowsPerTexture)
    {
        return new ScalarPair(
            1.0 / (columnsPerTexture * FloorSquareMeters),
            1.0 / (rowsPerTexture * FloorSquareMeters));
    }

    private static ScalarPair? CreateTextureOffsetValue(
        double columnsPerTexture,
        double rowsPerTexture,
        double offsetColumns,
        double offsetRows)
    {
        if (Math.Abs(offsetColumns) < 1e-9 && Math.Abs(offsetRows) < 1e-9)
        {
            return null;
        }

        return new ScalarPair(
            offsetColumns / columnsPerTexture,
            offsetRows / rowsPerTexture);
    }
}
