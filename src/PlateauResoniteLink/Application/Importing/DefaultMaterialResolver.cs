using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultMaterialResolver
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
            : request.FamilyOverride.SelectMember(commonMaterials, request.VariantSelectionKey);
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
            return DefaultMaterialFamilyOverride.Roof.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsRoadPackage(request.PackageName)
            || PlateauPackageCatalog.IsPathLikePackage(request.PackageName))
        {
            return request.PreferUvProjection
                ? DefaultMaterialFamilyOverride.RoadUv.SelectMember(commonMaterials, request.VariantSelectionKey)
                : DefaultMaterialFamilyOverride.RoadTriplanar.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsVegetationPackage(request.PackageName))
        {
            return DefaultMaterialFamilyOverride.Vegetation.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsCityFurniturePackage(request.PackageName))
        {
            return DefaultMaterialFamilyOverride.CityFurniture.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        return DefaultMaterialFamilyOverride.Other.SelectMember(commonMaterials, request.VariantSelectionKey);
    }

    private static DefaultCommonMaterialMember SelectBuildingFacadeMember(
        DefaultMaterialRequest request,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        BuildingAttributeContext attributes = request.BuildingAttributes;
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            request.FloorsAboveGround,
            request.MeasuredHeightMeters,
            request.GeometryHeightMeters,
            request.FootprintAreaSquareMeters);

        if (scale.Landmark)
        {
            return BuildingAttributePredicates.HasNightOccupancy(attributes)
                ? DefaultMaterialFamilyOverride.FacadeHighriseNightLow.SelectMember(commonMaterials, request.VariantSelectionKey)
                : DefaultMaterialFamilyOverride.FacadeHighriseGlass.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (scale.Highrise)
        {
            return BuildingAttributePredicates.HasNightOccupancy(attributes)
                ? DefaultMaterialFamilyOverride.FacadeHighriseNightLow.SelectMember(commonMaterials, request.VariantSelectionKey)
                : DefaultMaterialFamilyOverride.FacadeHighriseGlass.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (scale.Midrise && BuildingAttributePredicates.HasFacadeLikeMidriseUse(attributes))
        {
            return DefaultMaterialFamilyOverride.FacadeMidriseGrid.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasRawBuildingCode(attributes, "431")
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Warehouse)
            || BuildingAttributePredicates.HasRawBuildingCode(attributes, "441")
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Factory)
            || scale.LargeLowRise)
        {
            return commonMaterials.WallFactoryMetal.FactoryMetal;
        }

        if (BuildingAttributePredicates.HasRawBuildingCode(attributes, "451"))
        {
            return commonMaterials.WallWoodRural.WoodRuralLight;
        }

        if (BuildingAttributePredicates.HasBrickLikeStructure(attributes))
        {
            return DefaultMaterialFamilyOverride.WallBrickRetro.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Commercial)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Office))
        {
            return scale.LowRise
                ? DefaultMaterialFamilyOverride.WallCommercialPanel.SelectMember(commonMaterials, request.VariantSelectionKey)
                : DefaultMaterialFamilyOverride.WallRcPaintedMid.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Public)
            || BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Education))
        {
            return DefaultMaterialFamilyOverride.WallSchoolPublicBand.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.Apartment))
        {
            return scale.LowRise
                ? DefaultMaterialFamilyOverride.WallResidentialTileLow.SelectMember(commonMaterials, request.VariantSelectionKey)
                : DefaultMaterialFamilyOverride.WallApartmentTileMid.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.MixedResidential))
        {
            return scale.LowRise
                ? DefaultMaterialFamilyOverride.WallResidentialPlasterLow.SelectMember(commonMaterials, request.VariantSelectionKey)
                : DefaultMaterialFamilyOverride.WallApartmentTileMid.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.HasUse(attributes, PlateauBuildingUse.DetachedResidential))
        {
            return IsWeightedAlternate(request.VariantSelectionKey)
                ? DefaultMaterialFamilyOverride.WallResidentialTileLow.SelectMember(commonMaterials, request.VariantSelectionKey)
                : DefaultMaterialFamilyOverride.WallResidentialPlasterLow.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        if (BuildingAttributePredicates.IsRobustStructure(attributes) || scale.MidOrHighRise)
        {
            return DefaultMaterialFamilyOverride.WallRcPaintedMid.SelectMember(commonMaterials, request.VariantSelectionKey);
        }

        return DefaultMaterialFamilyOverride.WallResidentialPlasterLow.SelectMember(commonMaterials, request.VariantSelectionKey);
    }

    private static bool IsWeightedAlternate(string variantSelectionKey)
    {
        return StableVariantSelector.SelectBucket($"{variantSelectionKey}:residential-wall-weight", 5) == 0;
    }

    private static Float2 ToContractFloat2(Domain.Importing.ScalarPair value) => new(value.X, value.Y);
}
