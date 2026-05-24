using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultMaterialMemberSelector(
    CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
{
    private readonly CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials =
        commonMaterials ?? throw new ArgumentNullException(nameof(commonMaterials));
    private readonly BuildingFacadeMaterialSelector buildingFacadeSelector = new(commonMaterials);

    public DefaultCommonMaterialMember Select(DefaultMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.FamilyOverride is null
            ? SelectBundledMemberForRequest(request)
            : SelectFamilyOverrideMember(request.FamilyOverride, request.VariantSelectionKey);
    }

    private static bool ShouldUseBuildingFacade(DefaultMaterialRequest request)
    {
        return request.PreferUvProjection
            && PlateauPackageCatalog.IsBuildingPackage(request.PackageName)
            && request.SurfaceRole is DefaultMaterialSurfaceRole.Wall
                or DefaultMaterialSurfaceRole.Closure
                or DefaultMaterialSurfaceRole.Unknown;
    }

    private DefaultCommonMaterialMember SelectBundledMemberForRequest(DefaultMaterialRequest request)
    {
        if (ShouldUseBuildingFacade(request))
        {
            return buildingFacadeSelector.Select(request);
        }

        if (PlateauPackageCatalog.IsBuildingPackage(request.PackageName))
        {
            return SelectRoofMember(request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsRoadPackage(request.PackageName)
            || PlateauPackageCatalog.IsPathLikePackage(request.PackageName))
        {
            return request.PreferUvProjection
                ? SelectRoadUvMember(request.VariantSelectionKey)
                : SelectRoadTriplanarMember(request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsVegetationPackage(request.PackageName))
        {
            return SelectVegetationMember(request.VariantSelectionKey);
        }

        if (PlateauPackageCatalog.IsCityFurniturePackage(request.PackageName))
        {
            return SelectCityFurnitureMember(request.VariantSelectionKey);
        }

        return SelectOtherMember(request.VariantSelectionKey);
    }

    private DefaultCommonMaterialMember SelectFamilyOverrideMember(
        string family,
        string variantSelectionKey)
    {
        if (buildingFacadeSelector.SelectFamilyOverride(family, variantSelectionKey) is { } buildingFacadeMember)
        {
            return buildingFacadeMember;
        }

        return family switch
        {
            BundledDefaultMaterialFamilies.CityFurniture => SelectCityFurnitureMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.Other => SelectOtherMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.RoadTriplanar => SelectRoadTriplanarMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.RoadUv => SelectRoadUvMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.Roof => SelectRoofMember(variantSelectionKey),
            BundledDefaultMaterialFamilies.Vegetation => SelectVegetationMember(variantSelectionKey),
            _ => throw new InvalidOperationException(
                $"Bundled material family override '{family}' is not codebase-reachable and is not part of the common material catalog."),
        };
    }

    private DefaultCommonMaterialMember SelectCityFurnitureMember(string key) =>
        StableVariantSelector.SelectBucket(key, 6) switch
        {
            0 => commonMaterials.CityFurniture.Plaster002,
            1 => commonMaterials.CityFurniture.Plaster001,
            2 => commonMaterials.CityFurniture.Plaster003,
            3 => commonMaterials.CityFurniture.Plaster004,
            4 => commonMaterials.CityFurniture.Plaster005,
            _ => commonMaterials.CityFurniture.Plaster006,
        };

    private DefaultCommonMaterialMember SelectOtherMember(string key) =>
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

    private DefaultCommonMaterialMember SelectRoadTriplanarMember(string key) =>
        StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => commonMaterials.RoadTriplanar.Road012A,
            1 => commonMaterials.RoadTriplanar.Road013A,
            2 => commonMaterials.RoadTriplanar.Road014A,
            _ => commonMaterials.RoadTriplanar.Road015A,
        };

    private DefaultCommonMaterialMember SelectRoadUvMember(string key) =>
        StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => commonMaterials.RoadUv.Road012A,
            1 => commonMaterials.RoadUv.Road013A,
            2 => commonMaterials.RoadUv.Road014A,
            _ => commonMaterials.RoadUv.Road015A,
        };

    private DefaultCommonMaterialMember SelectRoofMember(string key) =>
        StableVariantSelector.SelectBucket(key, 4) switch
        {
            0 => commonMaterials.Roof.Concrete012,
            1 => commonMaterials.Roof.Concrete033,
            2 => commonMaterials.Roof.RoofingTiles012A,
            _ => commonMaterials.Roof.RoofingTiles014B,
        };

    private DefaultCommonMaterialMember SelectVegetationMember(string key) =>
        StableVariantSelector.SelectBucket(key, 2) == 0
            ? commonMaterials.Vegetation.Ground054
            : commonMaterials.Vegetation.Concrete012;

}
