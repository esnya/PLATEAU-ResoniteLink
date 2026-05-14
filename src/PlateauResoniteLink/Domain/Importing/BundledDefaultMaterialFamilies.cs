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

    private static readonly BundledDefaultMaterialProfile ConcreteDefaultTextureSet = new(BundledDefaultMaterialTiling.DefaultTilesPerMeterValue);
    private static readonly BundledDefaultMaterialProfile RoofingTiles012ATextureSet = new(CreateTilesPerMeterValue(2.9, 2.9));
    private static readonly BundledDefaultMaterialProfile RoofingTiles014BTextureSet = new(CreateTilesPerMeterValue(2.9, 2.9));
    private static readonly BundledDefaultMaterialProfile Plaster002TextureSet = new(CreateTilesPerMeterValue(2.5, 2.5));
    private static readonly BundledDefaultMaterialProfile Ground054TextureSet = new(CreateTilesPerMeterValue(3.5, 3.5));
    private static readonly BundledDefaultMaterialProfile RoadDefaultTextureSet = new(BundledDefaultMaterialTiling.DefaultTilesPerMeterValue);
    private static readonly BundledDefaultMaterialProfile TextureCanFacadeTextureSet = new(CreateTilesPerMeterValue(6.0, 6.0));

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> FacadeVariants =
    [
        new(
            BundledDefaultTextureAssets.Facade.Facade018A.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 6.0, rowsPerTexture: 6.0, offsetRows: 0.5)),
        new(
            BundledDefaultTextureAssets.Facade.Facade019A.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 6.0, rowsPerTexture: 6.0, offsetRows: 0.5)),
        new(
            BundledDefaultTextureAssets.Facade.Facade020A.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 6.0, rowsPerTexture: 6.0, offsetRows: 0.5)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallResidentialPlasterLowVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.ResidentialPlasterLow.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 3.0)),
        new(
            BundledDefaultTextureAssets.WallSkins.ResidentialPlasterDark.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 3.0),
            TextureSources: new(
                Emission: BundledDefaultTextureAssets.WallSkins.ResidentialPlasterLow.Emission,
                Metallic: BundledDefaultTextureAssets.WallSkins.ResidentialPlasterLow.Metallic)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallResidentialTileLowVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.ResidentialTileLow.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 3.0)),
        new(
            BundledDefaultTextureAssets.WallSkins.ResidentialTileDark.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 3.0),
            TextureSources: new(Emission: BundledDefaultTextureAssets.WallSkins.ResidentialTileLow.Emission)),
        new(
            BundledDefaultTextureAssets.WallSkins.ResidentialTileDarkIrregular.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 3.0),
            TextureSources: new(Emission: BundledDefaultTextureAssets.WallSkins.ResidentialPlasterLow.Emission)),
        new(
            BundledDefaultTextureAssets.WallSkins.ResidentialSidingBrickGray.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 3.0),
            TextureSources: new(Emission: BundledDefaultTextureAssets.WallSkins.ResidentialPlasterLow.Emission)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallApartmentTileMidVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.ApartmentTileMid.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 6.0)),
        new(
            BundledDefaultTextureAssets.WallSkins.ApartmentTileDark.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 6.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallRcPaintedMidVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.RcPaintedMid.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 5.0)),
        new(
            BundledDefaultTextureAssets.WallSkins.RcPaintedDark.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 5.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallFactoryMetalVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.FactoryMetal.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 2.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallCommercialPanelVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.CommercialPanel.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 5.0)),
        new(
            BundledDefaultTextureAssets.WallSkins.CommercialPanelDark.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 5.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallSchoolPublicBandVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.SchoolPublicBand.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 4.0)),
        new(
            BundledDefaultTextureAssets.WallSkins.SchoolPublicDark.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 4.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallBrickRetroVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.BrickRetro.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 4.0)),
        new(
            BundledDefaultTextureAssets.WallSkins.BrickDark.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 4.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> WallWoodRuralVariants =
    [
        new(
            BundledDefaultTextureAssets.WallSkins.WoodRuralLight.Albedo,
            CreateWallSkinFacadeFloorUnitTextureSet(storeysInTexture: 2.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> FacadeHighriseGlassVariants =
    [
        new(
            BundledDefaultTextureAssets.Facade.Facade001.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 16.0, rowsPerTexture: 10.0)),
        new(
            BundledDefaultTextureAssets.Facade.Facade005.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 32.0, rowsPerTexture: 24.0)),
        new(
            BundledDefaultTextureAssets.Facade.Facade006.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 14.0, rowsPerTexture: 8.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> FacadeHighriseNightLowVariants =
    [
        new(
            BundledDefaultTextureAssets.Facade.Facade002.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 16.0, rowsPerTexture: 10.0),
            TextureSources: new(
                Height: BundledDefaultTextureAssets.Facade.Facade001.Height,
                Metallic: BundledDefaultTextureAssets.Facade.Facade001.Metallic,
                Normal: BundledDefaultTextureAssets.Facade.Facade001.Normal)),
        new(
            BundledDefaultTextureAssets.Facade.Facade011.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 40.0, rowsPerTexture: 40.0)),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> FacadeMidriseGridVariants =
    [
        new(
            BundledDefaultTextureAssets.Facade.Facade014.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 32.0, rowsPerTexture: 32.0, offsetRows: 0.25)),
        new(
            BundledDefaultTextureAssets.Facade.Facade015.Albedo,
            CreateFacadeFloorUnitTextureSet(columnsPerTexture: 32.0, rowsPerTexture: 32.0, offsetRows: 0.25),
            TextureSources: new(
                Height: BundledDefaultTextureAssets.Facade.Facade014.Height,
                Metallic: BundledDefaultTextureAssets.Facade.Facade014.Metallic,
                Normal: BundledDefaultTextureAssets.Facade.Facade014.Normal)),
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
        new(
            BundledDefaultTextureAssets.Concrete.Concrete012.Albedo,
            ConcreteDefaultTextureSet),
        new(BundledDefaultTextureAssets.Concrete.Concrete033.Albedo, ConcreteDefaultTextureSet),
        new(BundledDefaultTextureAssets.Roof.RoofingTiles012A.Albedo, RoofingTiles012ATextureSet),
        new(BundledDefaultTextureAssets.Roof.RoofingTiles014B.Albedo, RoofingTiles014BTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> RoadVariants =
    [
        new(BundledDefaultTextureAssets.Road.Road012A.Albedo, RoadDefaultTextureSet),
        new(BundledDefaultTextureAssets.Road.Road013A.Albedo, RoadDefaultTextureSet),
        new(BundledDefaultTextureAssets.Road.Road014A.Albedo, RoadDefaultTextureSet),
        new(BundledDefaultTextureAssets.Road.Road015A.Albedo, RoadDefaultTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> VegetationVariants =
    [
        new(BundledDefaultTextureAssets.Ground.Ground054.Albedo, Ground054TextureSet),
        new(BundledDefaultTextureAssets.Concrete.Concrete012.Albedo, ConcreteDefaultTextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> CityFurnitureVariants =
    [
        new(BundledDefaultTextureAssets.Wall.Plaster002.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster001.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster003.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster004.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster005.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster006.Albedo, Plaster002TextureSet),
    ];

    public static readonly IReadOnlyList<BundledDefaultMaterialVariant> OtherVariants =
    [
        new(BundledDefaultTextureAssets.Concrete.Concrete012.Albedo, ConcreteDefaultTextureSet),
        new(BundledDefaultTextureAssets.Ground.Ground054.Albedo, Ground054TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster002.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster001.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster003.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster004.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster005.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.Wall.Plaster006.Albedo, Plaster002TextureSet),
        new(BundledDefaultTextureAssets.TextureCanFacade.Others0022.Albedo, TextureCanFacadeTextureSet),
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

        return variants[variantIndex].Albedo.LogicalPath;
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

    public static string GetVariantMaterialName(string family, int variantIndex)
    {
        _ = GetVariantDefinition(family, variantIndex);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{nameof(CommonMaterialMembers.Variant).ToLowerInvariant()}-{variantIndex}");
    }

    private static class CommonMaterialMembers
    {
        public const string Variant = "";
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
                variantsByTexturePath.TryAdd(variant.Albedo.LogicalPath, variant);
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
                GetVariantDefinitions(family).Select(static variant => variant.Albedo.LogicalPath).ToArray());
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

    private static BundledDefaultMaterialProfile CreateWallSkinFacadeFloorUnitTextureSet(
        double storeysInTexture)
    {
        return CreateFacadeFloorUnitTextureSet(
            columnsPerTexture: storeysInTexture + 1.0,
            rowsPerTexture: storeysInTexture + 1.0,
            offsetRows: 0.5);
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
