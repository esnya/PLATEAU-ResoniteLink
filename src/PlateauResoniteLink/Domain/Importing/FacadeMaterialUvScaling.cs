using System;

namespace PlateauResoniteLink.Domain.Importing;

public static class FacadeMaterialUvScaling
{
    public static readonly BundledDefaultMaterialProfile Facade001Profile = CreateProfile(
        columnsPerTexture: 16.0,
        rowsPerTexture: 10.0);

    public static readonly BundledDefaultMaterialProfile Facade018AProfile = CreateProfile(
        columnsPerTexture: 6.0,
        rowsPerTexture: 6.0,
        offsetRows: 0.5);

    public static readonly BundledDefaultMaterialProfile Facade019AProfile = CreateProfile(
        columnsPerTexture: 6.0,
        rowsPerTexture: 6.0,
        offsetRows: 0.5);

    public static readonly BundledDefaultMaterialProfile Facade020AProfile = CreateProfile(
        columnsPerTexture: 6.0,
        rowsPerTexture: 6.0,
        offsetRows: 0.5);

    public static BundledDefaultMaterialProfile GetBundledProfile(string texturePath)
    {
        return texturePath.ToLowerInvariant() switch
        {
            "default-materials/ambientcg/facade/facade001_2k-jpg_color.jpg" => Facade001Profile,
            "default-materials/ambientcg/facade/facade018a_2k-jpg_color.jpg" => Facade018AProfile,
            "default-materials/ambientcg/facade/facade019a_2k-jpg_color.jpg" => Facade019AProfile,
            "default-materials/ambientcg/facade/facade020a_2k-jpg_color.jpg" => Facade020AProfile,
            _ => Facade018AProfile,
        };
    }

    private static BundledDefaultMaterialProfile CreateProfile(
        double columnsPerTexture,
        double rowsPerTexture,
        double offsetColumns = 0.0,
        double offsetRows = 0.0)
    {
        return new BundledDefaultMaterialProfile(
            CreateFloorUnitScaleValue(columnsPerTexture, rowsPerTexture),
            CreateTextureOffsetValue(columnsPerTexture, rowsPerTexture, offsetColumns, offsetRows),
            ScaleSemantic: BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits);
    }

    private static ScalarPair CreateFloorUnitScaleValue(double columnsPerTexture, double rowsPerTexture)
    {
        return new ScalarPair(
            1.0 / columnsPerTexture,
            1.0 / rowsPerTexture);
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
