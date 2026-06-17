using System.Linq;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class Lod1RoofShapePolicyTests
{
    [Fact]
    public void SelectUsesExplicitSupportedRoofShape()
    {
        BuildingAttributeContext attributes = CreateAttributes(
            roofShape: CityGmlRoofShape.Gable);

        GeneratedLod1RoofShape shape = Lod1RoofShapePolicy.Select(
            slotKey: "bldg-explicit",
            attributes,
            geometryHeightMeters: 6.0,
            lengthMeters: 10.0,
            widthMeters: 8.0);

        Assert.Equal(GeneratedLod1RoofShape.Gable, shape);
    }

    [Fact]
    public void SelectInfersFlatForUrbanNonWoodBuilding()
    {
        BuildingAttributeContext attributes = CreateAttributes(
            cityGmlFunctionCodes: ["401"],
            structures: [PlateauBuildingStructure.ReinforcedConcrete]);

        GeneratedLod1RoofShape shape = Lod1RoofShapePolicy.Select(
            slotKey: "bldg-office",
            attributes,
            geometryHeightMeters: 8.0,
            lengthMeters: 12.0,
            widthMeters: 10.0);

        Assert.Equal(GeneratedLod1RoofShape.Flat, shape);
    }

    [Fact]
    public void SelectInfersShedForLongOtherRoofBuilding()
    {
        BuildingAttributeContext attributes = CreateAttributes(
            roofShape: CityGmlRoofShape.Other);

        GeneratedLod1RoofShape shape = Lod1RoofShapePolicy.Select(
            slotKey: "bldg-long-other",
            attributes,
            geometryHeightMeters: 6.0,
            lengthMeters: 18.0,
            widthMeters: 8.0);

        Assert.Equal(GeneratedLod1RoofShape.Shed, shape);
    }

    [Fact]
    public void SelectInfersHipForSmallResidentialSquareBuilding()
    {
        BuildingAttributeContext attributes = CreateAttributes(
            uses: [PlateauBuildingUse.DetachedResidential],
            footprintArea: 120.0);

        GeneratedLod1RoofShape shape = Lod1RoofShapePolicy.Select(
            slotKey: "bldg-residential",
            attributes,
            geometryHeightMeters: 6.0,
            lengthMeters: 10.0,
            widthMeters: 9.0);

        Assert.Equal(GeneratedLod1RoofShape.Hip, shape);
    }

    private static BuildingAttributeContext CreateAttributes(
        CityGmlRoofShape? roofShape = null,
        PlateauBuildingUse[]? uses = null,
        PlateauBuildingStructure[]? structures = null,
        string[]? cityGmlFunctionCodes = null,
        double? footprintArea = null)
    {
        return new BuildingAttributeContext(
            RoofShape: roofShape is null ? null : new BuildingCodeValue<CityGmlRoofShape>(roofShape.Value, "test"),
            Uses: CreateCodeValues(uses),
            DetailedUses: [],
            Structures: CreateCodeValues(structures),
            CityGmlClassCodes: [],
            CityGmlFunctionCodes: cityGmlFunctionCodes ?? [],
            MeasuredHeightMeters: null,
            StoreysAboveGround: null,
            StoreysBelowGround: null,
            BuildingFootprintArea: footprintArea.HasValue ? new BuildingMetricValue(footprintArea.Value) : null,
            BuildingRoofEdgeArea: null,
            BuildingHeight: null,
            EaveHeight: null);
    }

    private static BuildingCodeValue<T>[] CreateCodeValues<T>(T[]? values)
    {
        return values?.Select(static value => new BuildingCodeValue<T>(value, "test")).ToArray() ?? [];
    }
}
