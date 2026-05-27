using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class BuildingAttributePredicatesTests
{
    [Theory]
    [InlineData("403", (int)PlateauBuildingUse.Commercial)]
    [InlineData("403-1", (int)PlateauBuildingUse.Commercial)]
    [InlineData("112", (int)PlateauBuildingUse.Apartment)]
    [InlineData("131-office", (int)PlateauBuildingUse.Office)]
    [InlineData("181", (int)PlateauBuildingUse.Education)]
    public void HasUseMapsCityGmlFunctionCodesByBroadBuildingCode(string rawCode, int useValue)
    {
        PlateauBuildingUse use = (PlateauBuildingUse)useValue;
        BuildingAttributeContext attributes = BuildingAttributeContext.Empty with
        {
            CityGmlFunctionCodes = [rawCode],
        };

        Assert.True(BuildingAttributePredicates.HasUse(attributes, use));
    }

    [Theory]
    [InlineData("403-extra", "403")]
    [InlineData("401", "401")]
    public void HasRawBuildingCodeUsesFunctionAndClassBroadCodes(string rawCode, string expectedCode)
    {
        BuildingAttributeContext functionAttributes = BuildingAttributeContext.Empty with
        {
            CityGmlFunctionCodes = [rawCode],
        };
        BuildingAttributeContext classAttributes = BuildingAttributeContext.Empty with
        {
            CityGmlClassCodes = [rawCode],
        };

        Assert.True(BuildingAttributePredicates.HasRawBuildingCode(functionAttributes, expectedCode));
        Assert.True(BuildingAttributePredicates.HasRawBuildingCode(classAttributes, expectedCode));
    }

    [Theory]
    [InlineData((int)PlateauBuildingUse.Apartment)]
    [InlineData((int)PlateauBuildingUse.MixedResidential)]
    public void HasNightOccupancyIncludesResidentialSharedOccupancyUses(int useValue)
    {
        PlateauBuildingUse use = (PlateauBuildingUse)useValue;
        BuildingAttributeContext attributes = BuildingAttributeContext.Empty with
        {
            Uses = [new BuildingCodeValue<PlateauBuildingUse>(use, "test")],
        };

        Assert.True(BuildingAttributePredicates.HasNightOccupancy(attributes));
    }

    [Fact]
    public void HasNightOccupancyIncludesRaw403BuildingCode()
    {
        BuildingAttributeContext attributes = BuildingAttributeContext.Empty with
        {
            CityGmlFunctionCodes = ["403-extra"],
        };

        Assert.True(BuildingAttributePredicates.HasNightOccupancy(attributes));
    }

    [Theory]
    [InlineData((int)PlateauBuildingStructure.ReinforcedConcrete)]
    [InlineData((int)PlateauBuildingStructure.SteelReinforcedConcrete)]
    [InlineData((int)PlateauBuildingStructure.NonWood)]
    public void IsRobustStructureIncludesNonWoodAndReinforcedStructures(int structureValue)
    {
        PlateauBuildingStructure structure = (PlateauBuildingStructure)structureValue;
        BuildingAttributeContext attributes = BuildingAttributeContext.Empty with
        {
            Structures = [new BuildingCodeValue<PlateauBuildingStructure>(structure, "test")],
        };

        Assert.True(BuildingAttributePredicates.IsRobustStructure(attributes));
        Assert.False(BuildingAttributePredicates.HasBrickLikeStructure(attributes));
    }

    [Fact]
    public void HasBrickLikeStructureIncludesConcreteBlock()
    {
        BuildingAttributeContext attributes = BuildingAttributeContext.Empty with
        {
            Structures =
            [
                new BuildingCodeValue<PlateauBuildingStructure>(PlateauBuildingStructure.ConcreteBlock, "606"),
            ],
        };

        Assert.True(BuildingAttributePredicates.HasBrickLikeStructure(attributes));
        Assert.False(BuildingAttributePredicates.IsRobustStructure(attributes));
    }
}
