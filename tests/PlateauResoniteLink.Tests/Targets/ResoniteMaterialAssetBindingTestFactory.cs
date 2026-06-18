using PlateauResoniteLink.Application.Importing.Contracts;

using System;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

internal static class ResoniteMaterialAssetBindingTestFactory
{
    internal static ResoniteMaterialAssetBinding SharedBundled(string family, int variantIndex)
    {
        return ResoniteMaterialAssetBinding.SharedCommon(SelectBundledMember(family, variantIndex));
    }

    internal static ResoniteMaterialAssetBinding SharedGenericUv()
    {
        return ResoniteMaterialAssetBinding.SharedCommon(CommonMaterialCatalog.Create().Generic.Uv);
    }

    internal static DefaultCommonMaterialMember SelectBundledMember(string family, int variantIndex)
    {
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials = CommonMaterialCatalog.Create();
        return family switch
        {
            BundledDefaultMaterialFamilies.CityFurniture => variantIndex switch
            {
                0 => commonMaterials.CityFurniture.Plaster002,
                1 => commonMaterials.CityFurniture.Plaster001,
                2 => commonMaterials.CityFurniture.Plaster003,
                3 => commonMaterials.CityFurniture.Plaster004,
                4 => commonMaterials.CityFurniture.Plaster005,
                _ => commonMaterials.CityFurniture.Plaster006,
            },
            BundledDefaultMaterialFamilies.FacadeHighriseGlass => variantIndex switch
            {
                0 => commonMaterials.FacadeHighriseGlass.Facade001,
                1 => commonMaterials.FacadeHighriseGlass.Facade005,
                _ => commonMaterials.FacadeHighriseGlass.Facade006,
            },
            BundledDefaultMaterialFamilies.FacadeHighriseNightLow => variantIndex == 0
                ? commonMaterials.FacadeHighriseNightLow.Facade002
                : commonMaterials.FacadeHighriseNightLow.Facade011,
            BundledDefaultMaterialFamilies.FacadeMidriseGrid => variantIndex == 0
                ? commonMaterials.FacadeMidriseGrid.Facade014
                : commonMaterials.FacadeMidriseGrid.Facade015,
            BundledDefaultMaterialFamilies.Other => variantIndex switch
            {
                0 => commonMaterials.Other.Concrete012,
                1 => commonMaterials.Other.Ground054,
                2 => commonMaterials.Other.Plaster002,
                3 => commonMaterials.Other.Plaster001,
                4 => commonMaterials.Other.Plaster003,
                5 => commonMaterials.Other.Plaster004,
                6 => commonMaterials.Other.Plaster005,
                _ => commonMaterials.Other.Plaster006,
            },
            BundledDefaultMaterialFamilies.RoadTriplanar => variantIndex switch
            {
                0 => commonMaterials.RoadTriplanar.Road012A,
                1 => commonMaterials.RoadTriplanar.Road013A,
                2 => commonMaterials.RoadTriplanar.Road014A,
                _ => commonMaterials.RoadTriplanar.Road015A,
            },
            BundledDefaultMaterialFamilies.RoadUv => variantIndex switch
            {
                0 => commonMaterials.RoadUv.Road012A,
                1 => commonMaterials.RoadUv.Road013A,
                2 => commonMaterials.RoadUv.Road014A,
                _ => commonMaterials.RoadUv.Road015A,
            },
            BundledDefaultMaterialFamilies.Roof => variantIndex switch
            {
                0 => commonMaterials.Roof.Concrete012,
                1 => commonMaterials.Roof.Concrete033,
                2 => commonMaterials.Roof.RoofingTiles012A,
                _ => commonMaterials.Roof.RoofingTiles014B,
            },
            BundledDefaultMaterialFamilies.Vegetation => variantIndex == 0
                ? commonMaterials.Vegetation.Ground054
                : commonMaterials.Vegetation.Concrete012,
            BundledDefaultMaterialFamilies.WallResidentialPlasterLow => variantIndex == 0
                ? commonMaterials.WallResidentialPlasterLow.ResidentialPlasterLow
                : commonMaterials.WallResidentialPlasterLow.ResidentialPlasterDark,
            _ => throw new InvalidOperationException(
                $"Bundled family '{family}' variant {variantIndex} is not represented by this test factory."),
        };
    }
}
