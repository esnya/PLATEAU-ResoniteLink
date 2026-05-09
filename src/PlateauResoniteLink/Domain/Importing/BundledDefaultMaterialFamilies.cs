using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public static class BundledDefaultMaterialFamilies
{
    public const string Facade = "facade";
    public const string WallResidentialPlasterLow = "wall-res-plaster-low";
    public const string WallResidentialTileLow = "wall-res-tile-low";
    public const string WallApartmentTileMid = "wall-apartment-tile-mid";
    public const string WallRcPaintedMid = "wall-rc-painted-mid";
    public const string WallFactoryMetal = "wall-factory-metal";
    public const string WallCommercialPanel = "wall-commercial-panel";
    public const string WallSchoolPublicBand = "wall-school-public-band";
    public const string WallBrickRetro = "wall-brick-retro";
    public const string WallWoodRural = "wall-wood-rural";
    public const string FacadeHighriseGlass = "facade-highrise-glass";
    public const string FacadeHighriseNightLow = "facade-highrise-night-low";
    public const string FacadeMidriseGrid = "facade-midrise-grid";
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

    public static readonly IReadOnlyList<string> WallResidentialPlasterLowVariants =
    [
        "default-materials/wallskins/wall_res_plaster_low/basecolor.png",
        "default-materials/wallskins/wall_res_plaster_dark/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> WallResidentialTileLowVariants =
    [
        "default-materials/wallskins/wall_res_tile_low/basecolor.png",
        "default-materials/wallskins/wall_res_tile_dark/basecolor.png",
        "default-materials/wallskins/wall_res_tile_dark_irregular/basecolor.png",
        "default-materials/wallskins/wall_res_siding_brick_gray/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> WallApartmentTileMidVariants =
    [
        "default-materials/wallskins/wall_apartment_tile_mid/basecolor.png",
        "default-materials/wallskins/wall_apartment_tile_dark/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> WallRcPaintedMidVariants =
    [
        "default-materials/wallskins/wall_rc_painted_mid/basecolor.png",
        "default-materials/wallskins/wall_rc_painted_dark/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> WallFactoryMetalVariants =
    [
        "default-materials/wallskins/wall_factory_metal/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> WallCommercialPanelVariants =
    [
        "default-materials/wallskins/wall_commercial_panel/basecolor.png",
        "default-materials/wallskins/wall_commercial_panel_dark/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> WallSchoolPublicBandVariants =
    [
        "default-materials/wallskins/wall_school_public_band/basecolor.png",
        "default-materials/wallskins/wall_school_public_dark/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> WallBrickRetroVariants =
    [
        "default-materials/wallskins/wall_brick_retro/basecolor.png",
        "default-materials/wallskins/wall_brick_dark/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> WallWoodRuralVariants =
    [
        "default-materials/wallskins/wall_wood_rural_light/basecolor.png",
    ];

    public static readonly IReadOnlyList<string> FacadeHighriseGlassVariants =
    [
        "default-materials/ambientcg/facade/Facade001_2K-JPG_Color.jpg",
        "default-materials/ambientcg/facade/Facade005_2K-JPG_Color.jpg",
        "default-materials/ambientcg/facade/Facade006_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> FacadeHighriseNightLowVariants =
    [
        "default-materials/ambientcg/facade/Facade002_2K-JPG_Color.jpg",
        "default-materials/ambientcg/facade/Facade011_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> FacadeMidriseGridVariants =
    [
        "default-materials/ambientcg/facade/Facade014_2K-JPG_Color.jpg",
        "default-materials/ambientcg/facade/Facade015_2K-JPG_Color.jpg",
    ];

    public static readonly IReadOnlyList<string> BuildingWallSkinFamilies =
    [
        WallResidentialPlasterLow,
        WallResidentialTileLow,
        WallApartmentTileMid,
        WallRcPaintedMid,
        WallFactoryMetal,
        WallCommercialPanel,
        WallSchoolPublicBand,
        WallBrickRetro,
        WallWoodRural,
    ];

    public static readonly IReadOnlyList<string> BuildingFacadeFallbackFamilies =
    [
        FacadeHighriseGlass,
        FacadeHighriseNightLow,
        FacadeMidriseGrid,
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
            WallResidentialPlasterLow => WallResidentialPlasterLowVariants,
            WallResidentialTileLow => WallResidentialTileLowVariants,
            WallApartmentTileMid => WallApartmentTileMidVariants,
            WallRcPaintedMid => WallRcPaintedMidVariants,
            WallFactoryMetal => WallFactoryMetalVariants,
            WallCommercialPanel => WallCommercialPanelVariants,
            WallSchoolPublicBand => WallSchoolPublicBandVariants,
            WallBrickRetro => WallBrickRetroVariants,
            WallWoodRural => WallWoodRuralVariants,
            FacadeHighriseGlass => FacadeHighriseGlassVariants,
            FacadeHighriseNightLow => FacadeHighriseNightLowVariants,
            FacadeMidriseGrid => FacadeMidriseGridVariants,
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
