using System;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CommonMaterialDefinitions
{
    public static readonly CommonMaterialDefinition GenericUv = Generic("Uv", null);
    public static readonly CommonMaterialDefinition GenericTerrainAlignedUv = Generic(
        "TerrainAlignedUv",
        LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);
    public static readonly CommonMaterialDefinition VertexColorUv = VertexColor("Uv", null);
    public static readonly CommonMaterialDefinition VertexColorTerrainAlignedUv = VertexColor(
        "TerrainAlignedUv",
        LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset);

    public static readonly CommonMaterialDefinition CityFurniturePlaster002 = Bundled(BundledDefaultMaterialFamilies.CityFurniture, 0, "Plaster002");
    public static readonly CommonMaterialDefinition CityFurniturePlaster001 = Bundled(BundledDefaultMaterialFamilies.CityFurniture, 1, "Plaster001");
    public static readonly CommonMaterialDefinition CityFurniturePlaster003 = Bundled(BundledDefaultMaterialFamilies.CityFurniture, 2, "Plaster003");
    public static readonly CommonMaterialDefinition CityFurniturePlaster004 = Bundled(BundledDefaultMaterialFamilies.CityFurniture, 3, "Plaster004");
    public static readonly CommonMaterialDefinition CityFurniturePlaster005 = Bundled(BundledDefaultMaterialFamilies.CityFurniture, 4, "Plaster005");
    public static readonly CommonMaterialDefinition CityFurniturePlaster006 = Bundled(BundledDefaultMaterialFamilies.CityFurniture, 5, "Plaster006");
    public static readonly CommonMaterialDefinition FacadeHighriseGlass001 = Bundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0, "Facade001");
    public static readonly CommonMaterialDefinition FacadeHighriseGlass005 = Bundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 1, "Facade005");
    public static readonly CommonMaterialDefinition FacadeHighriseGlass006 = Bundled(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 2, "Facade006");
    public static readonly CommonMaterialDefinition FacadeHighriseNightLow002 = Bundled(BundledDefaultMaterialFamilies.FacadeHighriseNightLow, 0, "Facade002");
    public static readonly CommonMaterialDefinition FacadeHighriseNightLow011 = Bundled(BundledDefaultMaterialFamilies.FacadeHighriseNightLow, 1, "Facade011");
    public static readonly CommonMaterialDefinition FacadeMidriseGrid014 = Bundled(BundledDefaultMaterialFamilies.FacadeMidriseGrid, 0, "Facade014");
    public static readonly CommonMaterialDefinition FacadeMidriseGrid015 = Bundled(BundledDefaultMaterialFamilies.FacadeMidriseGrid, 1, "Facade015");
    public static readonly CommonMaterialDefinition OtherConcrete012 = Bundled(BundledDefaultMaterialFamilies.Other, 0, "Concrete012");
    public static readonly CommonMaterialDefinition OtherGround054 = Bundled(BundledDefaultMaterialFamilies.Other, 1, "Ground054");
    public static readonly CommonMaterialDefinition OtherPlaster002 = Bundled(BundledDefaultMaterialFamilies.Other, 2, "Plaster002");
    public static readonly CommonMaterialDefinition OtherPlaster001 = Bundled(BundledDefaultMaterialFamilies.Other, 3, "Plaster001");
    public static readonly CommonMaterialDefinition OtherPlaster003 = Bundled(BundledDefaultMaterialFamilies.Other, 4, "Plaster003");
    public static readonly CommonMaterialDefinition OtherPlaster004 = Bundled(BundledDefaultMaterialFamilies.Other, 5, "Plaster004");
    public static readonly CommonMaterialDefinition OtherPlaster005 = Bundled(BundledDefaultMaterialFamilies.Other, 6, "Plaster005");
    public static readonly CommonMaterialDefinition OtherPlaster006 = Bundled(BundledDefaultMaterialFamilies.Other, 7, "Plaster006");
    public static readonly CommonMaterialDefinition OtherTextureCanFacade0022 = Bundled(BundledDefaultMaterialFamilies.Other, 8, "TextureCanFacade0022");
    public static readonly CommonMaterialDefinition RoadTriplanar012A = Bundled(BundledDefaultMaterialFamilies.RoadTriplanar, 0, "Road012A");
    public static readonly CommonMaterialDefinition RoadTriplanar013A = Bundled(BundledDefaultMaterialFamilies.RoadTriplanar, 1, "Road013A");
    public static readonly CommonMaterialDefinition RoadTriplanar014A = Bundled(BundledDefaultMaterialFamilies.RoadTriplanar, 2, "Road014A");
    public static readonly CommonMaterialDefinition RoadTriplanar015A = Bundled(BundledDefaultMaterialFamilies.RoadTriplanar, 3, "Road015A");
    public static readonly CommonMaterialDefinition RoadUv012A = Bundled(BundledDefaultMaterialFamilies.RoadUv, 0, "Road012A");
    public static readonly CommonMaterialDefinition RoadUv013A = Bundled(BundledDefaultMaterialFamilies.RoadUv, 1, "Road013A");
    public static readonly CommonMaterialDefinition RoadUv014A = Bundled(BundledDefaultMaterialFamilies.RoadUv, 2, "Road014A");
    public static readonly CommonMaterialDefinition RoadUv015A = Bundled(BundledDefaultMaterialFamilies.RoadUv, 3, "Road015A");
    public static readonly CommonMaterialDefinition RoofConcrete012 = Bundled(BundledDefaultMaterialFamilies.Roof, 0, "Concrete012");
    public static readonly CommonMaterialDefinition RoofConcrete033 = Bundled(BundledDefaultMaterialFamilies.Roof, 1, "Concrete033");
    public static readonly CommonMaterialDefinition RoofRoofingTiles012A = Bundled(BundledDefaultMaterialFamilies.Roof, 2, "RoofingTiles012A");
    public static readonly CommonMaterialDefinition RoofRoofingTiles014B = Bundled(BundledDefaultMaterialFamilies.Roof, 3, "RoofingTiles014B");
    public static readonly CommonMaterialDefinition VegetationGround054 = Bundled(BundledDefaultMaterialFamilies.Vegetation, 0, "Ground054");
    public static readonly CommonMaterialDefinition VegetationConcrete012 = Bundled(BundledDefaultMaterialFamilies.Vegetation, 1, "Concrete012");
    public static readonly CommonMaterialDefinition WallApartmentTileMid = Bundled(BundledDefaultMaterialFamilies.WallApartmentTileMid, 0, "ApartmentTileMid");
    public static readonly CommonMaterialDefinition WallApartmentTileDark = Bundled(BundledDefaultMaterialFamilies.WallApartmentTileMid, 1, "ApartmentTileDark");
    public static readonly CommonMaterialDefinition WallBrickRetro = Bundled(BundledDefaultMaterialFamilies.WallBrickRetro, 0, "BrickRetro");
    public static readonly CommonMaterialDefinition WallBrickDark = Bundled(BundledDefaultMaterialFamilies.WallBrickRetro, 1, "BrickDark");
    public static readonly CommonMaterialDefinition WallCommercialPanel = Bundled(BundledDefaultMaterialFamilies.WallCommercialPanel, 0, "CommercialPanel");
    public static readonly CommonMaterialDefinition WallCommercialPanelDark = Bundled(BundledDefaultMaterialFamilies.WallCommercialPanel, 1, "CommercialPanelDark");
    public static readonly CommonMaterialDefinition WallFactoryMetal = Bundled(BundledDefaultMaterialFamilies.WallFactoryMetal, 0, "FactoryMetal");
    public static readonly CommonMaterialDefinition WallRcPaintedMid = Bundled(BundledDefaultMaterialFamilies.WallRcPaintedMid, 0, "RcPaintedMid");
    public static readonly CommonMaterialDefinition WallRcPaintedDark = Bundled(BundledDefaultMaterialFamilies.WallRcPaintedMid, 1, "RcPaintedDark");
    public static readonly CommonMaterialDefinition WallResidentialPlasterLow = Bundled(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 0, "ResidentialPlasterLow");
    public static readonly CommonMaterialDefinition WallResidentialPlasterDark = Bundled(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 1, "ResidentialPlasterDark");
    public static readonly CommonMaterialDefinition WallResidentialTileLow = Bundled(BundledDefaultMaterialFamilies.WallResidentialTileLow, 0, "ResidentialTileLow");
    public static readonly CommonMaterialDefinition WallResidentialTileDark = Bundled(BundledDefaultMaterialFamilies.WallResidentialTileLow, 1, "ResidentialTileDark");
    public static readonly CommonMaterialDefinition WallResidentialTileDarkIrregular = Bundled(BundledDefaultMaterialFamilies.WallResidentialTileLow, 2, "ResidentialTileDarkIrregular");
    public static readonly CommonMaterialDefinition WallResidentialSidingBrickGray = Bundled(BundledDefaultMaterialFamilies.WallResidentialTileLow, 3, "ResidentialSidingBrickGray");
    public static readonly CommonMaterialDefinition WallSchoolPublicBand = Bundled(BundledDefaultMaterialFamilies.WallSchoolPublicBand, 0, "SchoolPublicBand");
    public static readonly CommonMaterialDefinition WallSchoolPublicDark = Bundled(BundledDefaultMaterialFamilies.WallSchoolPublicBand, 1, "SchoolPublicDark");
    public static readonly CommonMaterialDefinition WallWoodRuralLight = Bundled(BundledDefaultMaterialFamilies.WallWoodRural, 0, "WoodRuralLight");

    private static BundledCommonMaterialDefinition Bundled(string family, int variantIndex, string memberName)
    {
        return new BundledCommonMaterialDefinition(
            GetBundledProjection(family),
            memberName,
            family,
            variantIndex);
    }

    private static GenericAlbedoCommonMaterialDefinition Generic(string memberName, MaterialDepthOffset? depthOffset)
    {
        return new GenericAlbedoCommonMaterialDefinition(
            memberName,
            depthOffset);
    }

    private static VertexColorCommonMaterialDefinition VertexColor(string memberName, MaterialDepthOffset? depthOffset)
    {
        return new VertexColorCommonMaterialDefinition(
            memberName,
            depthOffset);
    }

    private static MaterialProjection GetBundledProjection(string family)
    {
        return string.Equals(family, BundledDefaultMaterialFamilies.RoadUv, StringComparison.Ordinal)
            || BundledDefaultMaterialFamilies.BuildingFacadeFamilies.Contains(family, StringComparer.Ordinal)
            ? MaterialProjection.Uv
            : MaterialProjection.Triplanar;
    }
}
