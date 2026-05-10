using System;

namespace PlateauResoniteLink.Domain.Importing;

public static class FacadeMaterialUvScaling
{
    public static readonly BundledDefaultMaterialProfile Facade001Profile = CreateProfile(
        columnsPerTexture: 16.0,
        rowsPerTexture: 10.0);

    public static readonly BundledDefaultMaterialProfile Facade005Profile = CreateProfile(
        columnsPerTexture: 32.0,
        rowsPerTexture: 24.0);

    public static readonly BundledDefaultMaterialProfile Facade006Profile = CreateProfile(
        columnsPerTexture: 14.0,
        rowsPerTexture: 8.0);

    public static readonly BundledDefaultMaterialProfile Facade011Profile = CreateProfile(
        columnsPerTexture: 40.0,
        rowsPerTexture: 40.0);

    public static readonly BundledDefaultMaterialProfile Facade014Profile = CreateProfile(
        columnsPerTexture: 32.0,
        rowsPerTexture: 32.0,
        offsetRows: 0.25);

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
            "default-materials/ambientcg/facade/facade002_2k-jpg_color.jpg" => Facade001Profile,
            "default-materials/ambientcg/facade/facade005_2k-jpg_color.jpg" => Facade005Profile,
            "default-materials/ambientcg/facade/facade006_2k-jpg_color.jpg" => Facade006Profile,
            "default-materials/ambientcg/facade/facade011_2k-jpg_color.jpg" => Facade011Profile,
            "default-materials/ambientcg/facade/facade014_2k-jpg_color.jpg" => Facade014Profile,
            "default-materials/ambientcg/facade/facade015_2k-jpg_color.jpg" => Facade014Profile,
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
            CreateSquareTextureFloorUnitScaleValue(rowsPerTexture),
            CreateTextureOffsetValue(columnsPerTexture, rowsPerTexture, offsetColumns, offsetRows),
            ScaleSemantic: BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits);
    }

    private static ScalarPair CreateSquareTextureFloorUnitScaleValue(double rowsPerTexture)
    {
        return new ScalarPair(
            1.0 / rowsPerTexture,
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
