using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialFamilies
{
    public const string Facade = "facade";
    public const string Roof = "roof";
    public const string Road = "road";
    public const string Vegetation = "vegetation";
    public const string CityFurniture = "city-furniture";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> FacadeVariants =
    [
        "default-materials/ambientcg/facade/Facade018A_2K-JPG_Color.jpg",
        "default-materials/ambientcg/facade/Facade019A_2K-JPG_Color.jpg",
        "default-materials/ambientcg/facade/Facade020A_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> RoofVariants =
    [
        "default-materials/ambientcg/roof/Concrete012_2K-JPG_Color.jpg",
        "default-materials/ambientcg/roof/Concrete033_2K-JPG_Color.jpg",
        "default-materials/ambientcg/roof/RoofingTiles012A_2K-JPG_Color.jpg",
        "default-materials/ambientcg/roof/RoofingTiles014B_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> RoadVariants =
    [
        "default-materials/ambientcg/road/Road012A_2K-JPG_Color.jpg",
        "default-materials/ambientcg/road/Road013A_2K-JPG_Color.jpg",
        "default-materials/ambientcg/road/Road014A_2K-JPG_Color.jpg",
        "default-materials/ambientcg/road/Road015A_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> VegetationVariants =
    [
        "default-materials/ambientcg/other/Ground054_2K-JPG_Color.jpg",
        "default-materials/ambientcg/other/Concrete012_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> CityFurnitureVariants =
    [
        "default-materials/ambientcg/wall/Plaster002_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster001_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster003_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster004_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster005_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster006_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> OtherVariants =
    [
        "default-materials/ambientcg/other/Concrete012_2K-JPG_Color.jpg",
        "default-materials/ambientcg/other/Ground054_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster002_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster001_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster003_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster004_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster005_2K-JPG_Color.jpg",
        "default-materials/ambientcg/wall/Plaster006_2K-JPG_Color.jpg",
        "default-materials/texturecan/facade/Others0022_2K_Color.jpg",
    ];

    public static IReadOnlyList<string> GetVariants(string family)
    {
        return family switch
        {
            Facade => FacadeVariants,
            Roof => RoofVariants,
            Road => RoadVariants,
            Vegetation => VegetationVariants,
            CityFurniture => CityFurnitureVariants,
            Other => OtherVariants,
            _ => throw new InvalidOperationException($"Unknown bundled material family '{family}'."),
        };
    }

    public static string GetVariant(string family, int variantIndex)
    {
        IReadOnlyList<string> variants = GetVariants(family);
        if ((uint)variantIndex >= (uint)variants.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(variantIndex), variantIndex, $"Family '{family}' has {variants.Count} variants.");
        }

        return variants[variantIndex];
    }
}
