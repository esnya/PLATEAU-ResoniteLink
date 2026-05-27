using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultMaterialResolver : IDefaultMaterialResolver
{
    private readonly CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials;

    public DefaultMaterialResolver(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        this.commonMaterials = commonMaterials ?? throw new ArgumentNullException(nameof(commonMaterials));
    }

    public ResolvedMaterial ResolveMaterial(DefaultMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ResolveMaterialCore(request, commonMaterials);
    }

    internal static ResolvedMaterial ResolveMaterialCore(
        DefaultMaterialRequest request,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(commonMaterials);

        if (ShouldUseWireframeMaterial(request.PackageName))
        {
            return new ResolvedMaterial(
                MaterialType.Wireframe,
                TexturePayload: null,
                TextureSourceKind.Bundled,
                MaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                ReuseScope: MaterialReuseScope.PerObject);
        }

        if (request.TexturePayload is not null)
        {
            return new ResolvedMaterial(
                MaterialType.Standard,
                request.TexturePayload,
                TextureSourceKind.Dataset,
                MaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                ReuseScope: MaterialReuseScope.PerObject);
        }

        DefaultCommonMaterialMember commonMaterial = request.FamilyOverride is null
            ? SelectBundledMemberForRequest(request, commonMaterials)
            : SelectFamilyOverrideMember(commonMaterials, request.FamilyOverride, request.VariantSelectionKey);
        string family = commonMaterial.Family
            ?? throw new InvalidOperationException("Selected default material member is not a bundled material.");
        int bundledVariantIndex = commonMaterial.BundledVariantIndex
            ?? throw new InvalidOperationException("Selected bundled material member does not expose a variant index.");
        BundledDefaultMaterialVariant variant = commonMaterial.BundledVariant
            ?? throw new InvalidOperationException("Selected bundled material member does not expose a variant.");
        BundledDefaultMaterialProfile uvProfile = variant.TextureSet;
        Float2? textureOffset = uvProfile.TextureOffset is null ? null : ToContractFloat2(uvProfile.TextureOffset);

        return new ResolvedMaterial(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Bundled,
            request.PreferUvProjection ? MaterialProjection.Uv : MaterialProjection.Triplanar,
            family,
            ToContractFloat2(uvProfile.TextureScale),
            MaterialReuseScope.Shared,
            BundledVariantIndex: bundledVariantIndex,
            TextureOffset: textureOffset,
            CommonMaterial: commonMaterial);
    }

    private static bool ShouldUseWireframeMaterial(string packageName)
    {
        return PlateauPackageCatalog.IsWireframeOverlayPackage(packageName);
    }

    private static bool ShouldUseBuildingFacade(DefaultMaterialRequest request)
    {
        return request.PreferUvProjection
            && PlateauPackageCatalog.IsBuildingPackage(request.PackageName)
            && request.SurfaceRole is DefaultMaterialSurfaceRole.Wall
                or DefaultMaterialSurfaceRole.Closure
                or DefaultMaterialSurfaceRole.Unknown;
    }

    private static DefaultCommonMaterialMember SelectBundledMemberForRequest(
        DefaultMaterialRequest request,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        if (ShouldUseBuildingFacade(request))
        {
            return SelectBuildingFacadeMember(request, commonMaterials);
        }

        if (PlateauPackageCatalog.IsBuildingPackage(request.PackageName))
        {
            return SelectRoofMember(commonMaterials, request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsRoadPackage(request.PackageName)
            || PlateauPackageCatalog.IsPathLikePackage(request.PackageName))
        {
            return request.PreferUvProjection
                ? SelectRoadUvMember(commonMaterials, request.VariantSelectionKey)
                : SelectRoadTriplanarMember(commonMaterials, request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsVegetationPackage(request.PackageName))
        {
            return SelectVegetationMember(commonMaterials, request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsCityFurniturePackage(request.PackageName))
        {
            return SelectCityFurnitureMember(commonMaterials, request.VariantSelectionKey);
        }

        return SelectOtherMember(commonMaterials, request.VariantSelectionKey);
    }

    private static DefaultCommonMaterialMember SelectFamilyOverrideMember(
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        string family,
        string variantSelectionKey)
    {
        return family switch
        {
            BundledDefaultMaterialFamilies.CityFurniture => SelectCityFurnitureMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.FacadeHighriseGlass => SelectFacadeHighriseGlassMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.FacadeHighriseNightLow => SelectFacadeHighriseNightLowMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.FacadeMidriseGrid => SelectFacadeMidriseGridMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.Other => SelectOtherMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.RoadTriplanar => SelectRoadTriplanarMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.RoadUv => SelectRoadUvMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.Roof => SelectRoofMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.Vegetation => SelectVegetationMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.WallApartmentTileMid => SelectWallApartmentTileMidMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.WallBrickRetro => SelectWallBrickRetroMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.WallCommercialPanel => SelectWallCommercialPanelMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.WallFactoryMetal => commonMaterials.WallFactoryMetal.FactoryMetal,
            BundledDefaultMaterialFamilies.WallRcPaintedMid => SelectWallRcPaintedMidMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.WallResidentialPlasterLow => SelectWallResidentialPlasterLowMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.WallResidentialTileLow => SelectWallResidentialTileLowMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.WallSchoolPublicBand => SelectWallSchoolPublicBandMember(commonMaterials, variantSelectionKey),
            BundledDefaultMaterialFamilies.WallWoodRural => commonMaterials.WallWoodRural.WoodRuralLight,
            _ => throw new InvalidOperationException(
                $"Bundled material family override '{family}' is not codebase-reachable and is not part of the common material catalog."),
        };
    }

    private static DefaultCommonMaterialMember SelectBuildingFacadeMember(
        DefaultMaterialRequest request,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
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
                ? SelectFacadeHighriseNightLowMember(commonMaterials, request.VariantSelectionKey)
                : SelectFacadeHighriseGlassMember(commonMaterials, request.VariantSelectionKey);
        }

        if (highrise)
        {
            return BuildingAttributePredicates.HasNightOccupancy(attributes)
                ? SelectFacadeHighriseNightLowMember(commonMaterials, request.VariantSelectionKey)
                : SelectFacadeHighriseGlassMember(commonMaterials, request.VariantSelectionKey);
        }

        if (midrise && BuildingAttributePredicates.HasFacadeLikeMidriseUse(attributes))
        {
            return SelectFacadeMidriseGridMember(commonMaterials, request.VariantSelectionKey);
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
            return SelectWallBrickRetroMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Commercial)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Office))
        {
            return lowRise
                ? SelectWallCommercialPanelMember(commonMaterials, request.VariantSelectionKey)
                : SelectWallRcPaintedMidMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Public)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Education))
        {
            return SelectWallSchoolPublicBandMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Apartment))
        {
            return lowRise
                ? SelectWallResidentialTileLowMember(commonMaterials, request.VariantSelectionKey)
                : SelectWallApartmentTileMidMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.MixedResidential))
        {
            return lowRise
                ? SelectWallResidentialPlasterLowMember(commonMaterials, request.VariantSelectionKey)
                : SelectWallApartmentTileMidMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.DetachedResidential))
        {
            return IsWeightedAlternate(request.VariantSelectionKey)
                ? SelectWallResidentialTileLowMember(commonMaterials, request.VariantSelectionKey)
                : SelectWallResidentialPlasterLowMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.IsRobustStructure(attributes) || midOrHighRise)
        {
            return SelectWallRcPaintedMidMember(commonMaterials, request.VariantSelectionKey);
        }

        return SelectWallResidentialPlasterLowMember(commonMaterials, request.VariantSelectionKey);
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
        return StableVariantSelector.SelectBucket($"{variantSelectionKey}:residential-wall-weight", 5) == 0;
    }

    private static DefaultCommonMaterialMember SelectCityFurnitureMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 6) switch
        {
            0 => commonMaterials.CityFurniture.Plaster002,
            1 => commonMaterials.CityFurniture.Plaster001,
            2 => commonMaterials.CityFurniture.Plaster003,
            3 => commonMaterials.CityFurniture.Plaster004,
            4 => commonMaterials.CityFurniture.Plaster005,
            _ => commonMaterials.CityFurniture.Plaster006,
        };

    private static DefaultCommonMaterialMember SelectFacadeHighriseGlassMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 3) switch
        {
            0 => commonMaterials.FacadeHighriseGlass.Facade001,
            1 => commonMaterials.FacadeHighriseGlass.Facade005,
            _ => commonMaterials.FacadeHighriseGlass.Facade006,
        };

    private static DefaultCommonMaterialMember SelectFacadeHighriseNightLowMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.FacadeHighriseNightLow.Facade002
            : commonMaterials.FacadeHighriseNightLow.Facade011;

    private static DefaultCommonMaterialMember SelectFacadeMidriseGridMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.FacadeMidriseGrid.Facade014
            : commonMaterials.FacadeMidriseGrid.Facade015;

    private static DefaultCommonMaterialMember SelectOtherMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 9) switch
        {
            0 => commonMaterials.Other.Concrete012,
            1 => commonMaterials.Other.Ground054,
            2 => commonMaterials.Other.Plaster002,
            3 => commonMaterials.Other.Plaster001,
            4 => commonMaterials.Other.Plaster003,
            5 => commonMaterials.Other.Plaster004,
            6 => commonMaterials.Other.Plaster005,
            7 => commonMaterials.Other.Plaster006,
            _ => commonMaterials.Other.TextureCanFacade0022,
        };

    private static DefaultCommonMaterialMember SelectRoadTriplanarMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => commonMaterials.RoadTriplanar.Road012A,
            1 => commonMaterials.RoadTriplanar.Road013A,
            2 => commonMaterials.RoadTriplanar.Road014A,
            _ => commonMaterials.RoadTriplanar.Road015A,
        };

    private static DefaultCommonMaterialMember SelectRoadUvMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => commonMaterials.RoadUv.Road012A,
            1 => commonMaterials.RoadUv.Road013A,
            2 => commonMaterials.RoadUv.Road014A,
            _ => commonMaterials.RoadUv.Road015A,
        };

    private static DefaultCommonMaterialMember SelectRoofMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => commonMaterials.Roof.Concrete012,
            1 => commonMaterials.Roof.Concrete033,
            2 => commonMaterials.Roof.RoofingTiles012A,
            _ => commonMaterials.Roof.RoofingTiles014B,
        };

    private static DefaultCommonMaterialMember SelectVegetationMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.Vegetation.Ground054
            : commonMaterials.Vegetation.Concrete012;

    private static DefaultCommonMaterialMember SelectWallApartmentTileMidMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallApartmentTileMid.ApartmentTileMid
            : commonMaterials.WallApartmentTileMid.ApartmentTileDark;

    private static DefaultCommonMaterialMember SelectWallBrickRetroMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallBrickRetro.BrickRetro
            : commonMaterials.WallBrickRetro.BrickDark;

    private static DefaultCommonMaterialMember SelectWallCommercialPanelMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallCommercialPanel.CommercialPanel
            : commonMaterials.WallCommercialPanel.CommercialPanelDark;

    private static DefaultCommonMaterialMember SelectWallRcPaintedMidMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallRcPaintedMid.RcPaintedMid
            : commonMaterials.WallRcPaintedMid.RcPaintedDark;

    private static DefaultCommonMaterialMember SelectWallResidentialPlasterLowMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallResidentialPlasterLow.ResidentialPlasterLow
            : commonMaterials.WallResidentialPlasterLow.ResidentialPlasterDark;

    private static DefaultCommonMaterialMember SelectWallResidentialTileLowMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => commonMaterials.WallResidentialTileLow.ResidentialTileLow,
            1 => commonMaterials.WallResidentialTileLow.ResidentialTileDark,
            2 => commonMaterials.WallResidentialTileLow.ResidentialTileDarkIrregular,
            _ => commonMaterials.WallResidentialTileLow.ResidentialSidingBrickGray,
        };

    private static DefaultCommonMaterialMember SelectWallSchoolPublicBandMember(CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials, string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.WallSchoolPublicBand.SchoolPublicBand
            : commonMaterials.WallSchoolPublicBand.SchoolPublicDark;

    private static Float2 ToContractFloat2(Domain.Importing.ScalarPair value) => new(value.X, value.Y);
}
