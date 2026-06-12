using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultMaterialFamilyOverride
{
    public static readonly DefaultMaterialFamilyOverride CityFurniture = new(
        BundledDefaultMaterialFamilies.CityFurniture,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 6) switch
        {
            0 => catalog.CityFurniture.Plaster002,
            1 => catalog.CityFurniture.Plaster001,
            2 => catalog.CityFurniture.Plaster003,
            3 => catalog.CityFurniture.Plaster004,
            4 => catalog.CityFurniture.Plaster005,
            _ => catalog.CityFurniture.Plaster006,
        });

    public static readonly DefaultMaterialFamilyOverride FacadeHighriseGlass = new(
        BundledDefaultMaterialFamilies.FacadeHighriseGlass,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 3) switch
        {
            0 => catalog.FacadeHighriseGlass.Facade001,
            1 => catalog.FacadeHighriseGlass.Facade005,
            _ => catalog.FacadeHighriseGlass.Facade006,
        });

    public static readonly DefaultMaterialFamilyOverride FacadeHighriseNightLow = new(
        BundledDefaultMaterialFamilies.FacadeHighriseNightLow,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.FacadeHighriseNightLow.Facade002
            : catalog.FacadeHighriseNightLow.Facade011);

    public static readonly DefaultMaterialFamilyOverride FacadeMidriseGrid = new(
        BundledDefaultMaterialFamilies.FacadeMidriseGrid,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.FacadeMidriseGrid.Facade014
            : catalog.FacadeMidriseGrid.Facade015);

    public static readonly DefaultMaterialFamilyOverride Other = new(
        BundledDefaultMaterialFamilies.Other,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 8) switch
        {
            0 => catalog.Other.Concrete012,
            1 => catalog.Other.Ground054,
            2 => catalog.Other.Plaster002,
            3 => catalog.Other.Plaster001,
            4 => catalog.Other.Plaster003,
            5 => catalog.Other.Plaster004,
            6 => catalog.Other.Plaster005,
            _ => catalog.Other.Plaster006,
        });

    public static readonly DefaultMaterialFamilyOverride RoadTriplanar = new(
        BundledDefaultMaterialFamilies.RoadTriplanar,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => catalog.RoadTriplanar.Road012A,
            1 => catalog.RoadTriplanar.Road013A,
            2 => catalog.RoadTriplanar.Road014A,
            _ => catalog.RoadTriplanar.Road015A,
        });

    public static readonly DefaultMaterialFamilyOverride RoadUv = new(
        BundledDefaultMaterialFamilies.RoadUv,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => catalog.RoadUv.Road012A,
            1 => catalog.RoadUv.Road013A,
            2 => catalog.RoadUv.Road014A,
            _ => catalog.RoadUv.Road015A,
        });

    public static readonly DefaultMaterialFamilyOverride Roof = new(
        BundledDefaultMaterialFamilies.Roof,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => catalog.Roof.Concrete012,
            1 => catalog.Roof.Concrete033,
            2 => catalog.Roof.RoofingTiles012A,
            _ => catalog.Roof.RoofingTiles014B,
        });

    public static readonly DefaultMaterialFamilyOverride Vegetation = new(
        BundledDefaultMaterialFamilies.Vegetation,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.Vegetation.Ground054
            : catalog.Vegetation.Concrete012);

    public static readonly DefaultMaterialFamilyOverride WallApartmentTileMid = new(
        BundledDefaultMaterialFamilies.WallApartmentTileMid,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.WallApartmentTileMid.ApartmentTileMid
            : catalog.WallApartmentTileMid.ApartmentTileDark);

    public static readonly DefaultMaterialFamilyOverride WallBrickRetro = new(
        BundledDefaultMaterialFamilies.WallBrickRetro,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.WallBrickRetro.BrickRetro
            : catalog.WallBrickRetro.BrickDark);

    public static readonly DefaultMaterialFamilyOverride WallCommercialPanel = new(
        BundledDefaultMaterialFamilies.WallCommercialPanel,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.WallCommercialPanel.CommercialPanel
            : catalog.WallCommercialPanel.CommercialPanelDark);

    public static readonly DefaultMaterialFamilyOverride WallFactoryMetal = new(
        BundledDefaultMaterialFamilies.WallFactoryMetal,
        static (catalog, _) => catalog.WallFactoryMetal.FactoryMetal);

    public static readonly DefaultMaterialFamilyOverride WallRcPaintedMid = new(
        BundledDefaultMaterialFamilies.WallRcPaintedMid,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.WallRcPaintedMid.RcPaintedMid
            : catalog.WallRcPaintedMid.RcPaintedDark);

    public static readonly DefaultMaterialFamilyOverride WallResidentialPlasterLow = new(
        BundledDefaultMaterialFamilies.WallResidentialPlasterLow,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.WallResidentialPlasterLow.ResidentialPlasterLow
            : catalog.WallResidentialPlasterLow.ResidentialPlasterDark);

    public static readonly DefaultMaterialFamilyOverride WallResidentialTileLow = new(
        BundledDefaultMaterialFamilies.WallResidentialTileLow,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => catalog.WallResidentialTileLow.ResidentialTileLow,
            1 => catalog.WallResidentialTileLow.ResidentialTileDark,
            2 => catalog.WallResidentialTileLow.ResidentialTileDarkIrregular,
            _ => catalog.WallResidentialTileLow.ResidentialSidingBrickGray,
        });

    public static readonly DefaultMaterialFamilyOverride WallSchoolPublicBand = new(
        BundledDefaultMaterialFamilies.WallSchoolPublicBand,
        static (catalog, key) => StableVariantSelector.SelectBucket(key, 2) == 0
            ? catalog.WallSchoolPublicBand.SchoolPublicBand
            : catalog.WallSchoolPublicBand.SchoolPublicDark);

    public static readonly DefaultMaterialFamilyOverride WallWoodRural = new(
        BundledDefaultMaterialFamilies.WallWoodRural,
        static (catalog, _) => catalog.WallWoodRural.WoodRuralLight);

    public static readonly IReadOnlyList<DefaultMaterialFamilyOverride> All =
    [
        CityFurniture,
        FacadeHighriseGlass,
        FacadeHighriseNightLow,
        FacadeMidriseGrid,
        Other,
        RoadTriplanar,
        RoadUv,
        Roof,
        Vegetation,
        WallApartmentTileMid,
        WallBrickRetro,
        WallCommercialPanel,
        WallFactoryMetal,
        WallRcPaintedMid,
        WallResidentialPlasterLow,
        WallResidentialTileLow,
        WallSchoolPublicBand,
        WallWoodRural,
    ];

    private readonly Func<CommonMaterialCatalog<DefaultCommonMaterialMember>, string, DefaultCommonMaterialMember> selectMember;

    private DefaultMaterialFamilyOverride(
        string family,
        Func<CommonMaterialCatalog<DefaultCommonMaterialMember>, string, DefaultCommonMaterialMember> selectMember)
    {
        Family = family;
        this.selectMember = selectMember;
    }

    public string Family { get; }

    internal DefaultCommonMaterialMember SelectMember(
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        string variantSelectionKey)
    {
        return selectMember(commonMaterials, variantSelectionKey);
    }
}
