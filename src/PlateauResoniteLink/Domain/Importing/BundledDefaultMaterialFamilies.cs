using System;
using System.Collections.Generic;
using System.Linq;

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
    public const string RoadUv = "road-uv";
    public const string RoadTriplanar = "road-triplanar";
    public const string Vegetation = "vegetation";
    public const string CityFurniture = "city-furniture";
    public const string Other = "other";

    private static readonly BundledDefaultMaterialProfile Facade001TextureSet = CreateFacadeFloorUnitTextureSet(
        columnsPerTexture: 16.0,
        rowsPerTexture: 10.0);

    private static readonly BundledDefaultMaterialProfile Facade005TextureSet = CreateFacadeFloorUnitTextureSet(
        columnsPerTexture: 32.0,
        rowsPerTexture: 24.0);

    private static readonly BundledDefaultMaterialProfile Facade006TextureSet = CreateFacadeFloorUnitTextureSet(
        columnsPerTexture: 14.0,
        rowsPerTexture: 8.0);

    private static readonly BundledDefaultMaterialProfile Facade011TextureSet = CreateFacadeFloorUnitTextureSet(
        columnsPerTexture: 40.0,
        rowsPerTexture: 40.0);

    private static readonly BundledDefaultMaterialProfile Facade014TextureSet = CreateFacadeFloorUnitTextureSet(
        columnsPerTexture: 32.0,
        rowsPerTexture: 32.0,
        offsetRows: 0.25);

    private static readonly BundledDefaultMaterialProfile Facade018ATextureSet = CreateFacadeFloorUnitTextureSet(
        columnsPerTexture: 6.0,
        rowsPerTexture: 6.0,
        offsetRows: 0.5);

    private static readonly BundledDefaultMaterialProfile Facade019ATextureSet = CreateFacadeFloorUnitTextureSet(
        columnsPerTexture: 6.0,
        rowsPerTexture: 6.0,
        offsetRows: 0.5);

    private static readonly BundledDefaultMaterialProfile Facade020ATextureSet = CreateFacadeFloorUnitTextureSet(
        columnsPerTexture: 6.0,
        rowsPerTexture: 6.0,
        offsetRows: 0.5);

    private static readonly BundledDefaultMaterialProfile GeneratedFacadeTextureSet = new(
        new ScalarPair(1.0 / 6.0, 1.0 / 6.0),
        TextureOffset: new ScalarPair(0.0, 0.5 / 6.0),
        ScaleSemantic: BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits);

    private static readonly BundledDefaultMaterialProfile ConcreteDefaultTextureSet = new(BundledDefaultMaterialTiling.DefaultTilesPerMeterValue);
    private static readonly BundledDefaultMaterialProfile RoofingTiles012ATextureSet = new(CreateTilesPerMeterValue(2.9, 2.9));
    private static readonly BundledDefaultMaterialProfile RoofingTiles014BTextureSet = new(CreateTilesPerMeterValue(2.9, 2.9));
    private static readonly BundledDefaultMaterialProfile Plaster002TextureSet = new(CreateTilesPerMeterValue(2.5, 2.5));
    private static readonly BundledDefaultMaterialProfile Ground054TextureSet = new(CreateTilesPerMeterValue(3.5, 3.5));
    private static readonly BundledDefaultMaterialProfile RoadDefaultTextureSet = new(BundledDefaultMaterialTiling.DefaultTilesPerMeterValue);
    private static readonly BundledDefaultMaterialProfile TextureCanFacadeTextureSet = new(CreateTilesPerMeterValue(6.0, 6.0));

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> FacadeVariants =
    [
        new("default-materials/ambientcg/facade/Facade018A_2K-JPG_Color.jpg", Facade018ATextureSet),
        new("default-materials/ambientcg/facade/Facade019A_2K-JPG_Color.jpg", Facade019ATextureSet),
        new("default-materials/ambientcg/facade/Facade020A_2K-JPG_Color.jpg", Facade020ATextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallResidentialPlasterLowVariants =
    [
        new("default-materials/wallskins/wall_res_plaster_low/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_res_plaster_dark/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallResidentialTileLowVariants =
    [
        new("default-materials/wallskins/wall_res_tile_low/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_res_tile_dark/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_res_tile_dark_irregular/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_res_siding_brick_gray/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallApartmentTileMidVariants =
    [
        new("default-materials/wallskins/wall_apartment_tile_mid/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_apartment_tile_dark/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallRcPaintedMidVariants =
    [
        new("default-materials/wallskins/wall_rc_painted_mid/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_rc_painted_dark/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallFactoryMetalVariants =
    [
        new("default-materials/wallskins/wall_factory_metal/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallCommercialPanelVariants =
    [
        new("default-materials/wallskins/wall_commercial_panel/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_commercial_panel_dark/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallSchoolPublicBandVariants =
    [
        new("default-materials/wallskins/wall_school_public_band/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_school_public_dark/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallBrickRetroVariants =
    [
        new("default-materials/wallskins/wall_brick_retro/basecolor.png", GeneratedFacadeTextureSet),
        new("default-materials/wallskins/wall_brick_dark/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallWoodRuralVariants =
    [
        new("default-materials/wallskins/wall_wood_rural_light/basecolor.png", GeneratedFacadeTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> FacadeHighriseGlassVariants =
    [
        new("default-materials/ambientcg/facade/Facade001_2K-JPG_Color.jpg", Facade001TextureSet),
        new("default-materials/ambientcg/facade/Facade005_2K-JPG_Color.jpg", Facade005TextureSet),
        new("default-materials/ambientcg/facade/Facade006_2K-JPG_Color.jpg", Facade006TextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> FacadeHighriseNightLowVariants =
    [
        new("default-materials/ambientcg/facade/Facade002_2K-JPG_Color.jpg", Facade001TextureSet),
        new("default-materials/ambientcg/facade/Facade011_2K-JPG_Color.jpg", Facade011TextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> FacadeMidriseGridVariants =
    [
        new("default-materials/ambientcg/facade/Facade014_2K-JPG_Color.jpg", Facade014TextureSet),
        new("default-materials/ambientcg/facade/Facade015_2K-JPG_Color.jpg", Facade014TextureSet),
    ];

    public static readonly IReadOnlyList<string> BuildingFacadeFamilies =
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
        FacadeHighriseGlass,
        FacadeHighriseNightLow,
        FacadeMidriseGrid,
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> RoofVariants =
    [
        new("default-materials/ambientcg/roof/Concrete012_2K-JPG_Color.jpg", ConcreteDefaultTextureSet),
        new("default-materials/ambientcg/roof/Concrete033_2K-JPG_Color.jpg", ConcreteDefaultTextureSet),
        new("default-materials/ambientcg/roof/RoofingTiles012A_2K-JPG_Color.jpg", RoofingTiles012ATextureSet),
        new("default-materials/ambientcg/roof/RoofingTiles014B_2K-JPG_Color.jpg", RoofingTiles014BTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> RoadVariants =
    [
        new("default-materials/ambientcg/road/Road012A_2K-JPG_Color.jpg", RoadDefaultTextureSet),
        new("default-materials/ambientcg/road/Road013A_2K-JPG_Color.jpg", RoadDefaultTextureSet),
        new("default-materials/ambientcg/road/Road014A_2K-JPG_Color.jpg", RoadDefaultTextureSet),
        new("default-materials/ambientcg/road/Road015A_2K-JPG_Color.jpg", RoadDefaultTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> VegetationVariants =
    [
        new("default-materials/ambientcg/other/Ground054_2K-JPG_Color.jpg", Ground054TextureSet),
        new("default-materials/ambientcg/other/Concrete012_2K-JPG_Color.jpg", ConcreteDefaultTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> CityFurnitureVariants =
    [
        new("default-materials/ambientcg/wall/Plaster002_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster001_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster003_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster004_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster005_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster006_2K-JPG_Color.jpg", Plaster002TextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> OtherVariants =
    [
        new("default-materials/ambientcg/other/Concrete012_2K-JPG_Color.jpg", ConcreteDefaultTextureSet),
        new("default-materials/ambientcg/other/Ground054_2K-JPG_Color.jpg", Ground054TextureSet),
        new("default-materials/ambientcg/wall/Plaster002_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster001_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster003_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster004_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster005_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/ambientcg/wall/Plaster006_2K-JPG_Color.jpg", Plaster002TextureSet),
        new("default-materials/texturecan/facade/Others0022_2K_Color.jpg", TextureCanFacadeTextureSet),
    ];

    private static readonly Dictionary<string, BundledDefaultMaterialVariant> VariantsByTexturePath = CreateVariantsByTexturePath();
    private static readonly Dictionary<string, IReadOnlyList<string>> VariantTexturePathsByFamily = CreateVariantTexturePathsByFamily();

    public static IReadOnlyList<string> GetVariants(string family)
    {
        return VariantTexturePathsByFamily.TryGetValue(family, out IReadOnlyList<string>? variants)
            ? variants
            : throw new InvalidOperationException($"Unknown bundled material family '{family}'.");
    }

    public static IReadOnlyList<BundledDefaultMaterialVariant> GetVariantDefinitions(string family)
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
            RoadUv => RoadVariants,
            RoadTriplanar => RoadVariants,
            Vegetation => VegetationVariants,
            CityFurniture => CityFurnitureVariants,
            Other => OtherVariants,
            _ => throw new InvalidOperationException($"Unknown bundled material family '{family}'."),
        };
    }

    public static string GetVariant(string family, int variantIndex)
    {
        IReadOnlyList<BundledDefaultMaterialVariant> variants = GetVariantDefinitions(family);
        if ((uint)variantIndex >= (uint)variants.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(variantIndex), variantIndex, $"Family '{family}' has {variants.Count} variants.");
        }

        return variants[variantIndex].TexturePath;
    }

    public static BundledDefaultMaterialVariant GetVariantDefinition(string family, int variantIndex)
    {
        IReadOnlyList<BundledDefaultMaterialVariant> variants = GetVariantDefinitions(family);
        if ((uint)variantIndex >= (uint)variants.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(variantIndex), variantIndex, $"Family '{family}' has {variants.Count} variants.");
        }

        return variants[variantIndex];
    }

    public static bool TryGetVariantDefinition(string texturePath, out BundledDefaultMaterialVariant variant)
    {
        return VariantsByTexturePath.TryGetValue(texturePath, out variant!);
    }

    private static Dictionary<string, BundledDefaultMaterialVariant> CreateVariantsByTexturePath()
    {
        Dictionary<string, BundledDefaultMaterialVariant> variantsByTexturePath = new(StringComparer.OrdinalIgnoreCase);
        foreach (string family in GetAllFamilies())
        {
            foreach (BundledDefaultMaterialVariant variant in GetVariantDefinitions(family))
            {
                variantsByTexturePath.TryAdd(variant.TexturePath, variant);
            }
        }

        return variantsByTexturePath;
    }

    private static Dictionary<string, IReadOnlyList<string>> CreateVariantTexturePathsByFamily()
    {
        Dictionary<string, IReadOnlyList<string>> variantTexturePathsByFamily = new(StringComparer.Ordinal);
        foreach (string family in GetAllFamilies())
        {
            variantTexturePathsByFamily.Add(
                family,
                GetVariantDefinitions(family).Select(static variant => variant.TexturePath).ToArray());
        }

        return variantTexturePathsByFamily;
    }

    private static IReadOnlyList<string> GetAllFamilies()
    {
        return
        [
            Facade,
            WallResidentialPlasterLow,
            WallResidentialTileLow,
            WallApartmentTileMid,
            WallRcPaintedMid,
            WallFactoryMetal,
            WallCommercialPanel,
            WallSchoolPublicBand,
            WallBrickRetro,
            WallWoodRural,
            FacadeHighriseGlass,
            FacadeHighriseNightLow,
            FacadeMidriseGrid,
            Roof,
            RoadUv,
            RoadTriplanar,
            Vegetation,
            CityFurniture,
            Other,
        ];
    }

    private static BundledDefaultMaterialProfile CreateFacadeFloorUnitTextureSet(
        double columnsPerTexture,
        double rowsPerTexture,
        double offsetColumns = 0.0,
        double offsetRows = 0.0)
    {
        return new BundledDefaultMaterialProfile(
            new ScalarPair(1.0 / rowsPerTexture, 1.0 / rowsPerTexture),
            CreateTextureOffsetValue(columnsPerTexture, rowsPerTexture, offsetColumns, offsetRows),
            ScaleSemantic: BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits);
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

        return new ScalarPair(offsetColumns / columnsPerTexture, offsetRows / rowsPerTexture);
    }

    private static ScalarPair CreateTilesPerMeterValue(double widthMeters, double heightMeters)
    {
        return new ScalarPair(1.0 / widthMeters, 1.0 / heightMeters);
    }
}
