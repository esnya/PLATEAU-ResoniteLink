using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

public sealed class CommonMaterialCatalog<TItem>
{
    private readonly CommonMaterialCatalogMember<TItem>[] allMembers;
    private readonly CommonMaterialCatalogMember<TItem>[] members;
    private readonly Dictionary<CommonMaterialDefinition, TItem> itemsByDefinition;

    internal CommonMaterialCatalog(Func<CommonMaterialDefinition, TItem> create)
        : this(create, activeDefinitions: null)
    {
    }

    private CommonMaterialCatalog(
        Func<CommonMaterialDefinition, TItem> create,
        IReadOnlySet<CommonMaterialDefinition>? activeDefinitions)
    {
        ArgumentNullException.ThrowIfNull(create);

        Generic = new CommonGenericMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.GenericUv),
            create(CommonMaterialDefinitions.GenericTerrainAlignedUv));
        VertexColor = new CommonVertexColorMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.VertexColorUv),
            create(CommonMaterialDefinitions.VertexColorTerrainAlignedUv));

        CityFurniture = new CommonCityFurnitureMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.CityFurniturePlaster002),
            create(CommonMaterialDefinitions.CityFurniturePlaster001),
            create(CommonMaterialDefinitions.CityFurniturePlaster003),
            create(CommonMaterialDefinitions.CityFurniturePlaster004),
            create(CommonMaterialDefinitions.CityFurniturePlaster005),
            create(CommonMaterialDefinitions.CityFurniturePlaster006));
        FacadeHighriseGlass = new CommonFacadeHighriseGlassMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.FacadeHighriseGlass001),
            create(CommonMaterialDefinitions.FacadeHighriseGlass005),
            create(CommonMaterialDefinitions.FacadeHighriseGlass006));
        FacadeHighriseNightLow = new CommonFacadeHighriseNightLowMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.FacadeHighriseNightLow002),
            create(CommonMaterialDefinitions.FacadeHighriseNightLow011));
        FacadeMidriseGrid = new CommonFacadeMidriseGridMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.FacadeMidriseGrid014),
            create(CommonMaterialDefinitions.FacadeMidriseGrid015));
        Other = new CommonOtherMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.OtherConcrete012),
            create(CommonMaterialDefinitions.OtherGround054),
            create(CommonMaterialDefinitions.OtherPlaster002),
            create(CommonMaterialDefinitions.OtherPlaster001),
            create(CommonMaterialDefinitions.OtherPlaster003),
            create(CommonMaterialDefinitions.OtherPlaster004),
            create(CommonMaterialDefinitions.OtherPlaster005),
            create(CommonMaterialDefinitions.OtherPlaster006),
            create(CommonMaterialDefinitions.OtherTextureCanFacade0022));
        RoadTriplanar = new CommonRoadMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.RoadTriplanar012A),
            create(CommonMaterialDefinitions.RoadTriplanar013A),
            create(CommonMaterialDefinitions.RoadTriplanar014A),
            create(CommonMaterialDefinitions.RoadTriplanar015A));
        RoadUv = new CommonRoadMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.RoadUv012A),
            create(CommonMaterialDefinitions.RoadUv013A),
            create(CommonMaterialDefinitions.RoadUv014A),
            create(CommonMaterialDefinitions.RoadUv015A));
        Roof = new CommonRoofMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.RoofConcrete012),
            create(CommonMaterialDefinitions.RoofConcrete033),
            create(CommonMaterialDefinitions.RoofRoofingTiles012A),
            create(CommonMaterialDefinitions.RoofRoofingTiles014B));
        Vegetation = new CommonVegetationMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.VegetationGround054),
            create(CommonMaterialDefinitions.VegetationConcrete012));
        WallApartmentTileMid = new CommonWallApartmentTileMidMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallApartmentTileMid),
            create(CommonMaterialDefinitions.WallApartmentTileDark));
        WallBrickRetro = new CommonWallBrickRetroMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallBrickRetro),
            create(CommonMaterialDefinitions.WallBrickDark));
        WallCommercialPanel = new CommonWallCommercialPanelMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallCommercialPanel),
            create(CommonMaterialDefinitions.WallCommercialPanelDark));
        WallFactoryMetal = new CommonWallFactoryMetalMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallFactoryMetal));
        WallRcPaintedMid = new CommonWallRcPaintedMidMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallRcPaintedMid),
            create(CommonMaterialDefinitions.WallRcPaintedDark));
        WallResidentialPlasterLow = new CommonWallResidentialPlasterLowMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallResidentialPlasterLow),
            create(CommonMaterialDefinitions.WallResidentialPlasterDark));
        WallResidentialTileLow = new CommonWallResidentialTileLowMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallResidentialTileLow),
            create(CommonMaterialDefinitions.WallResidentialTileDark),
            create(CommonMaterialDefinitions.WallResidentialTileDarkIrregular),
            create(CommonMaterialDefinitions.WallResidentialSidingBrickGray));
        WallSchoolPublicBand = new CommonWallSchoolPublicBandMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallSchoolPublicBand),
            create(CommonMaterialDefinitions.WallSchoolPublicDark));
        WallWoodRural = new CommonWallWoodRuralMaterialCatalog<TItem>(
            create(CommonMaterialDefinitions.WallWoodRuralLight));

        allMembers =
        [
            Member(CommonMaterialDefinitions.CityFurniturePlaster002, CityFurniture.Plaster002),
            Member(CommonMaterialDefinitions.CityFurniturePlaster001, CityFurniture.Plaster001),
            Member(CommonMaterialDefinitions.CityFurniturePlaster003, CityFurniture.Plaster003),
            Member(CommonMaterialDefinitions.CityFurniturePlaster004, CityFurniture.Plaster004),
            Member(CommonMaterialDefinitions.CityFurniturePlaster005, CityFurniture.Plaster005),
            Member(CommonMaterialDefinitions.CityFurniturePlaster006, CityFurniture.Plaster006),
            Member(CommonMaterialDefinitions.FacadeHighriseGlass001, FacadeHighriseGlass.Facade001),
            Member(CommonMaterialDefinitions.FacadeHighriseGlass005, FacadeHighriseGlass.Facade005),
            Member(CommonMaterialDefinitions.FacadeHighriseGlass006, FacadeHighriseGlass.Facade006),
            Member(CommonMaterialDefinitions.FacadeHighriseNightLow002, FacadeHighriseNightLow.Facade002),
            Member(CommonMaterialDefinitions.FacadeHighriseNightLow011, FacadeHighriseNightLow.Facade011),
            Member(CommonMaterialDefinitions.FacadeMidriseGrid014, FacadeMidriseGrid.Facade014),
            Member(CommonMaterialDefinitions.FacadeMidriseGrid015, FacadeMidriseGrid.Facade015),
            Member(CommonMaterialDefinitions.OtherConcrete012, Other.Concrete012),
            Member(CommonMaterialDefinitions.OtherGround054, Other.Ground054),
            Member(CommonMaterialDefinitions.OtherPlaster002, Other.Plaster002),
            Member(CommonMaterialDefinitions.OtherPlaster001, Other.Plaster001),
            Member(CommonMaterialDefinitions.OtherPlaster003, Other.Plaster003),
            Member(CommonMaterialDefinitions.OtherPlaster004, Other.Plaster004),
            Member(CommonMaterialDefinitions.OtherPlaster005, Other.Plaster005),
            Member(CommonMaterialDefinitions.OtherPlaster006, Other.Plaster006),
            Member(CommonMaterialDefinitions.OtherTextureCanFacade0022, Other.TextureCanFacade0022),
            Member(CommonMaterialDefinitions.RoadTriplanar012A, RoadTriplanar.Road012A),
            Member(CommonMaterialDefinitions.RoadTriplanar013A, RoadTriplanar.Road013A),
            Member(CommonMaterialDefinitions.RoadTriplanar014A, RoadTriplanar.Road014A),
            Member(CommonMaterialDefinitions.RoadTriplanar015A, RoadTriplanar.Road015A),
            Member(CommonMaterialDefinitions.RoadUv012A, RoadUv.Road012A),
            Member(CommonMaterialDefinitions.RoadUv013A, RoadUv.Road013A),
            Member(CommonMaterialDefinitions.RoadUv014A, RoadUv.Road014A),
            Member(CommonMaterialDefinitions.RoadUv015A, RoadUv.Road015A),
            Member(CommonMaterialDefinitions.RoofConcrete012, Roof.Concrete012),
            Member(CommonMaterialDefinitions.RoofConcrete033, Roof.Concrete033),
            Member(CommonMaterialDefinitions.RoofRoofingTiles012A, Roof.RoofingTiles012A),
            Member(CommonMaterialDefinitions.RoofRoofingTiles014B, Roof.RoofingTiles014B),
            Member(CommonMaterialDefinitions.VegetationGround054, Vegetation.Ground054),
            Member(CommonMaterialDefinitions.VegetationConcrete012, Vegetation.Concrete012),
            Member(CommonMaterialDefinitions.WallApartmentTileMid, WallApartmentTileMid.ApartmentTileMid),
            Member(CommonMaterialDefinitions.WallApartmentTileDark, WallApartmentTileMid.ApartmentTileDark),
            Member(CommonMaterialDefinitions.WallBrickRetro, WallBrickRetro.BrickRetro),
            Member(CommonMaterialDefinitions.WallBrickDark, WallBrickRetro.BrickDark),
            Member(CommonMaterialDefinitions.WallCommercialPanel, WallCommercialPanel.CommercialPanel),
            Member(CommonMaterialDefinitions.WallCommercialPanelDark, WallCommercialPanel.CommercialPanelDark),
            Member(CommonMaterialDefinitions.WallFactoryMetal, WallFactoryMetal.FactoryMetal),
            Member(CommonMaterialDefinitions.WallRcPaintedMid, WallRcPaintedMid.RcPaintedMid),
            Member(CommonMaterialDefinitions.WallRcPaintedDark, WallRcPaintedMid.RcPaintedDark),
            Member(CommonMaterialDefinitions.WallResidentialPlasterLow, WallResidentialPlasterLow.ResidentialPlasterLow),
            Member(CommonMaterialDefinitions.WallResidentialPlasterDark, WallResidentialPlasterLow.ResidentialPlasterDark),
            Member(CommonMaterialDefinitions.WallResidentialTileLow, WallResidentialTileLow.ResidentialTileLow),
            Member(CommonMaterialDefinitions.WallResidentialTileDark, WallResidentialTileLow.ResidentialTileDark),
            Member(CommonMaterialDefinitions.WallResidentialTileDarkIrregular, WallResidentialTileLow.ResidentialTileDarkIrregular),
            Member(CommonMaterialDefinitions.WallResidentialSidingBrickGray, WallResidentialTileLow.ResidentialSidingBrickGray),
            Member(CommonMaterialDefinitions.WallSchoolPublicBand, WallSchoolPublicBand.SchoolPublicBand),
            Member(CommonMaterialDefinitions.WallSchoolPublicDark, WallSchoolPublicBand.SchoolPublicDark),
            Member(CommonMaterialDefinitions.WallWoodRuralLight, WallWoodRural.WoodRuralLight),
            Member(CommonMaterialDefinitions.GenericUv, Generic.Uv),
            Member(CommonMaterialDefinitions.GenericTerrainAlignedUv, Generic.TerrainAlignedUv),
            Member(CommonMaterialDefinitions.VertexColorUv, VertexColor.Uv),
            Member(CommonMaterialDefinitions.VertexColorTerrainAlignedUv, VertexColor.TerrainAlignedUv),
        ];
        members = activeDefinitions is null
            ? allMembers
            : allMembers
                .Where(member => activeDefinitions.Contains(member.Definition))
                .ToArray();
        itemsByDefinition = new Dictionary<CommonMaterialDefinition, TItem>(ReferenceEqualityComparer.Instance);
        foreach (CommonMaterialCatalogMember<TItem> member in members)
        {
            itemsByDefinition.Add(member.Definition, member.Item);
        }
    }

    public CommonGenericMaterialCatalog<TItem> Generic { get; }

    public CommonVertexColorMaterialCatalog<TItem> VertexColor { get; }

    public CommonCityFurnitureMaterialCatalog<TItem> CityFurniture { get; }

    public CommonFacadeHighriseGlassMaterialCatalog<TItem> FacadeHighriseGlass { get; }

    public CommonFacadeHighriseNightLowMaterialCatalog<TItem> FacadeHighriseNightLow { get; }

    public CommonFacadeMidriseGridMaterialCatalog<TItem> FacadeMidriseGrid { get; }

    public CommonOtherMaterialCatalog<TItem> Other { get; }

    public CommonRoadMaterialCatalog<TItem> RoadTriplanar { get; }

    public CommonRoadMaterialCatalog<TItem> RoadUv { get; }

    public CommonRoofMaterialCatalog<TItem> Roof { get; }

    public CommonVegetationMaterialCatalog<TItem> Vegetation { get; }

    public CommonWallApartmentTileMidMaterialCatalog<TItem> WallApartmentTileMid { get; }

    public CommonWallBrickRetroMaterialCatalog<TItem> WallBrickRetro { get; }

    public CommonWallCommercialPanelMaterialCatalog<TItem> WallCommercialPanel { get; }

    public CommonWallFactoryMetalMaterialCatalog<TItem> WallFactoryMetal { get; }

    public CommonWallRcPaintedMidMaterialCatalog<TItem> WallRcPaintedMid { get; }

    public CommonWallResidentialPlasterLowMaterialCatalog<TItem> WallResidentialPlasterLow { get; }

    public CommonWallResidentialTileLowMaterialCatalog<TItem> WallResidentialTileLow { get; }

    public CommonWallSchoolPublicBandMaterialCatalog<TItem> WallSchoolPublicBand { get; }

    public CommonWallWoodRuralMaterialCatalog<TItem> WallWoodRural { get; }

    internal int Count => members.Length;

    internal CommonMaterialCatalog<TOut> Map<TOut>(Func<TItem, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Dictionary<CommonMaterialDefinition, TOut> mapped = new(ReferenceEqualityComparer.Instance);
        foreach (CommonMaterialCatalogMember<TItem> member in members)
        {
            mapped.Add(member.Definition, map(member.Item));
        }

        return new CommonMaterialCatalog<TOut>(
            definition => mapped.TryGetValue(definition, out TOut? value) ? value : default!,
            CreateActiveDefinitionSet());
    }

    internal async ValueTask<CommonMaterialCatalog<TOut>> MapAsync<TOut>(
        Func<TItem, CancellationToken, ValueTask<TOut>> map,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(map);
        Dictionary<CommonMaterialDefinition, TOut> mapped = new(ReferenceEqualityComparer.Instance);
        foreach (CommonMaterialCatalogMember<TItem> member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mapped.Add(member.Definition, await map(member.Item, cancellationToken).ConfigureAwait(false));
        }

        return new CommonMaterialCatalog<TOut>(
            definition => mapped.TryGetValue(definition, out TOut? value) ? value : default!,
            CreateActiveDefinitionSet());
    }

    internal IReadOnlyList<CommonMaterialCatalogMember<TItem>> EnumerateMembers() => members;

    internal TItem[] EnumerateItems()
    {
        TItem[] items = new TItem[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            items[i] = members[i].Item;
        }

        return items;
    }

    internal TItem Get(CommonMaterialDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return itemsByDefinition[definition];
    }

    internal CommonMaterialCatalog<TItem> FilterToDefinitions(IEnumerable<CommonMaterialDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        HashSet<CommonMaterialDefinition> activeDefinitions = new(ReferenceEqualityComparer.Instance);
        foreach (CommonMaterialDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!itemsByDefinition.ContainsKey(definition))
            {
                throw new ArgumentException($"Common material definition '{definition.MemberName}' is not active in this catalog.", nameof(definitions));
            }

            activeDefinitions.Add(definition);
        }

        return new CommonMaterialCatalog<TItem>(
            definition => itemsByDefinition.TryGetValue(definition, out TItem? value) ? value : default!,
            activeDefinitions);
    }

    private HashSet<CommonMaterialDefinition> CreateActiveDefinitionSet()
    {
        HashSet<CommonMaterialDefinition> activeDefinitions = new(ReferenceEqualityComparer.Instance);
        foreach (CommonMaterialCatalogMember<TItem> member in members)
        {
            activeDefinitions.Add(member.Definition);
        }

        return activeDefinitions;
    }

    private static CommonMaterialCatalogMember<TItem> Member(CommonMaterialDefinition definition, TItem item) => new(definition, item);
}

internal static class CommonMaterialCatalog
{
    public static CommonMaterialCatalog<DefaultCommonMaterialMember> Create()
    {
        return new CommonMaterialCatalog<DefaultCommonMaterialMember>(DefaultCommonMaterialMember.Create);
    }
}

internal sealed record CommonMaterialCatalogMember<TItem>(
    CommonMaterialDefinition Definition,
    TItem Item);

public sealed record CommonGenericMaterialCatalog<TItem>(
    TItem Uv,
    TItem TerrainAlignedUv);

public sealed record CommonVertexColorMaterialCatalog<TItem>(
    TItem Uv,
    TItem TerrainAlignedUv);

public sealed record CommonCityFurnitureMaterialCatalog<TItem>(
    TItem Plaster002,
    TItem Plaster001,
    TItem Plaster003,
    TItem Plaster004,
    TItem Plaster005,
    TItem Plaster006);

public sealed record CommonFacadeHighriseGlassMaterialCatalog<TItem>(
    TItem Facade001,
    TItem Facade005,
    TItem Facade006);

public sealed record CommonFacadeHighriseNightLowMaterialCatalog<TItem>(
    TItem Facade002,
    TItem Facade011);

public sealed record CommonFacadeMidriseGridMaterialCatalog<TItem>(
    TItem Facade014,
    TItem Facade015);

public sealed record CommonOtherMaterialCatalog<TItem>(
    TItem Concrete012,
    TItem Ground054,
    TItem Plaster002,
    TItem Plaster001,
    TItem Plaster003,
    TItem Plaster004,
    TItem Plaster005,
    TItem Plaster006,
    TItem TextureCanFacade0022);

public sealed record CommonRoadMaterialCatalog<TItem>(
    TItem Road012A,
    TItem Road013A,
    TItem Road014A,
    TItem Road015A);

public sealed record CommonRoofMaterialCatalog<TItem>(
    TItem Concrete012,
    TItem Concrete033,
    TItem RoofingTiles012A,
    TItem RoofingTiles014B);

public sealed record CommonVegetationMaterialCatalog<TItem>(
    TItem Ground054,
    TItem Concrete012);

public sealed record CommonWallApartmentTileMidMaterialCatalog<TItem>(
    TItem ApartmentTileMid,
    TItem ApartmentTileDark);

public sealed record CommonWallBrickRetroMaterialCatalog<TItem>(
    TItem BrickRetro,
    TItem BrickDark);

public sealed record CommonWallCommercialPanelMaterialCatalog<TItem>(
    TItem CommercialPanel,
    TItem CommercialPanelDark);

public sealed record CommonWallFactoryMetalMaterialCatalog<TItem>(
    TItem FactoryMetal);

public sealed record CommonWallRcPaintedMidMaterialCatalog<TItem>(
    TItem RcPaintedMid,
    TItem RcPaintedDark);

public sealed record CommonWallResidentialPlasterLowMaterialCatalog<TItem>(
    TItem ResidentialPlasterLow,
    TItem ResidentialPlasterDark);

public sealed record CommonWallResidentialTileLowMaterialCatalog<TItem>(
    TItem ResidentialTileLow,
    TItem ResidentialTileDark,
    TItem ResidentialTileDarkIrregular,
    TItem ResidentialSidingBrickGray);

public sealed record CommonWallSchoolPublicBandMaterialCatalog<TItem>(
    TItem SchoolPublicBand,
    TItem SchoolPublicDark);

public sealed record CommonWallWoodRuralMaterialCatalog<TItem>(
    TItem WoodRuralLight);
