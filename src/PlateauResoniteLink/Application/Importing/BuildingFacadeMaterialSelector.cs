using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class BuildingFacadeMaterialSelector(
    CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
{
    private readonly CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials =
        commonMaterials ?? throw new ArgumentNullException(nameof(commonMaterials));

    public DefaultCommonMaterialMember Select(DefaultMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        BuildingAttributeContext attributes = request.BuildingAttributes ?? BuildingAttributeContext.Empty;
        int? floorCount = request.FloorsAboveGround;
        double? heightMeters = GetEffectiveHeightMeters(request);
        double? footprintArea = request.FootprintAreaSquareMeters;
        bool lowRise = IsLowRise(floorCount, heightMeters);
        bool midOrHighRise = IsMidOrHighRise(floorCount, heightMeters);
        bool midrise = IsMidrise(floorCount, heightMeters);
        bool highrise = IsHighrise(floorCount, heightMeters);
        bool landmark = IsLandmarkScale(floorCount, heightMeters);
        bool largeLowRise = lowRise && footprintArea is >= 1000.0;

        if (landmark)
        {
            return BuildingAttributePredicates.HasNightOccupancy(attributes)
                ? SelectFacadeHighriseNightLowMember(request.VariantSelectionKey)
                : SelectFacadeHighriseGlassMember(request.VariantSelectionKey);
        }

        if (highrise)
        {
            return BuildingAttributePredicates.HasNightOccupancy(attributes)
                ? SelectFacadeHighriseNightLowMember(request.VariantSelectionKey)
                : SelectFacadeHighriseGlassMember(request.VariantSelectionKey);
        }

        if (midrise && BuildingAttributePredicates.HasFacadeLikeMidriseUse(attributes))
        {
            return SelectFacadeMidriseGridMember(request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasRawBuildingCode(attributes, "431")
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Warehouse)
            || BuildingAttributePredicates.HasRawBuildingCode(attributes, "441")
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Factory)
            || largeLowRise)
        {
            return commonMaterials.WallFactoryMetal.FactoryMetal;
        }

        if (BuildingAttributePredicates.HasRawBuildingCode(attributes, "451"))
        {
            return commonMaterials.WallWoodRural.WoodRuralLight;
        }

        if (BuildingAttributePredicates.HasBrickLikeStructure(attributes))
        {
            return SelectWallBrickRetroMember(request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Commercial)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Office))
        {
            return lowRise
                ? SelectWallCommercialPanelMember(request.VariantSelectionKey)
                : SelectWallRcPaintedMidMember(request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Public)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Education))
        {
            return SelectWallSchoolPublicBandMember(request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Apartment))
        {
            return lowRise
                ? SelectWallResidentialTileLowMember(request.VariantSelectionKey)
                : SelectWallApartmentTileMidMember(request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.MixedResidential))
        {
            return lowRise
                ? SelectWallResidentialPlasterLowMember(request.VariantSelectionKey)
                : SelectWallApartmentTileMidMember(request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.DetachedResidential))
        {
            return IsWeightedAlternate(request.VariantSelectionKey)
                ? SelectWallResidentialTileLowMember(request.VariantSelectionKey)
                : SelectWallResidentialPlasterLowMember(request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasRobustStructure(attributes) || midOrHighRise)
        {
            return SelectWallRcPaintedMidMember(request.VariantSelectionKey);
        }

        return SelectWallResidentialPlasterLowMember(request.VariantSelectionKey);
    }

    public DefaultCommonMaterialMember? SelectFamilyOverride(string family, string variantSelectionKey)
    {
        return family switch
        {
            BundledDefaultMaterialFamilies.FacadeHighriseGlass => SelectFacadeHighriseGlassMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.FacadeHighriseNightLow => SelectFacadeHighriseNightLowMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.FacadeMidriseGrid => SelectFacadeMidriseGridMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.WallApartmentTileMid => SelectWallApartmentTileMidMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.WallBrickRetro => SelectWallBrickRetroMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.WallCommercialPanel => SelectWallCommercialPanelMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.WallFactoryMetal => commonMaterials.WallFactoryMetal.FactoryMetal,
            BundledDefaultMaterialFamilies.WallRcPaintedMid => SelectWallRcPaintedMidMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.WallResidentialPlasterLow => SelectWallResidentialPlasterLowMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.WallResidentialTileLow => SelectWallResidentialTileLowMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.WallSchoolPublicBand => SelectWallSchoolPublicBandMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.WallWoodRural => commonMaterials.WallWoodRural.WoodRuralLight,
            _ => null,
        };
    }

    private static bool IsLowRise(int? floorCount, double? heightMeters)
    {
        return (!FacadeFloorMetrics.IsUsableFloorCount(floorCount) || floorCount <= 3)
            && (!heightMeters.HasValue || heightMeters.Value < 12.0);
    }

    private static bool IsMidOrHighRise(int? floorCount, double? heightMeters)
    {
        return (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount >= 4)
            || heightMeters is >= 12.0;
    }

    private static bool IsMidrise(int? floorCount, double? heightMeters)
    {
        return (heightMeters is >= 25.0 and < 80.0)
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount is >= 8 and < 20);
    }

    private static bool IsHighrise(int? floorCount, double? heightMeters)
    {
        return (heightMeters is >= 80.0 and < 150.0)
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount is >= 20 and < 35);
    }

    private static bool IsLandmarkScale(int? floorCount, double? heightMeters)
    {
        return heightMeters is >= 150.0
            || (FacadeFloorMetrics.IsUsableFloorCount(floorCount) && floorCount >= 35);
    }

    private static double? GetEffectiveHeightMeters(DefaultMaterialRequest request)
    {
        return TryGetPositiveValue(request.MeasuredHeightMeters)
            ?? TryGetPositiveValue(request.GeometryHeightMeters);
    }

    private static double? TryGetPositiveValue(double? value)
    {
        return value is > 0.0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? value.Value
            : null;
    }

    private static bool IsWeightedAlternate(string variantSelectionKey)
    {
        return StableVariantSelector.IsWeightedAlternate(variantSelectionKey, "residential-wall-weight", 5);
    }

    private DefaultCommonMaterialMember SelectFacadeHighriseGlassMember(string key) =>
        StableVariantSelector.SelectBucket(key, 3) switch
        {
            0 => commonMaterials.FacadeHighriseGlass.Facade001,
            1 => commonMaterials.FacadeHighriseGlass.Facade005,
            _ => commonMaterials.FacadeHighriseGlass.Facade006,
        };

    private DefaultCommonMaterialMember SelectFacadeHighriseNightLowMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.FacadeHighriseNightLow.Facade002
            : commonMaterials.FacadeHighriseNightLow.Facade011;

    private DefaultCommonMaterialMember SelectFacadeMidriseGridMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.FacadeMidriseGrid.Facade014
            : commonMaterials.FacadeMidriseGrid.Facade015;

    private DefaultCommonMaterialMember SelectWallApartmentTileMidMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallApartmentTileMid.ApartmentTileMid
            : commonMaterials.WallApartmentTileMid.ApartmentTileDark;

    private DefaultCommonMaterialMember SelectWallBrickRetroMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallBrickRetro.BrickRetro
            : commonMaterials.WallBrickRetro.BrickDark;

    private DefaultCommonMaterialMember SelectWallCommercialPanelMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallCommercialPanel.CommercialPanel
            : commonMaterials.WallCommercialPanel.CommercialPanelDark;

    private DefaultCommonMaterialMember SelectWallRcPaintedMidMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallRcPaintedMid.RcPaintedMid
            : commonMaterials.WallRcPaintedMid.RcPaintedDark;

    private DefaultCommonMaterialMember SelectWallResidentialPlasterLowMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallResidentialPlasterLow.ResidentialPlasterLow
            : commonMaterials.WallResidentialPlasterLow.ResidentialPlasterDark;

    private DefaultCommonMaterialMember SelectWallResidentialTileLowMember(string key) =>
        StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => commonMaterials.WallResidentialTileLow.ResidentialTileLow,
            1 => commonMaterials.WallResidentialTileLow.ResidentialTileDark,
            2 => commonMaterials.WallResidentialTileLow.ResidentialTileDarkIrregular,
            _ => commonMaterials.WallResidentialTileLow.ResidentialSidingBrickGray,
        };

    private DefaultCommonMaterialMember SelectWallSchoolPublicBandMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallSchoolPublicBand.SchoolPublicBand
            : commonMaterials.WallSchoolPublicBand.SchoolPublicDark;
}
