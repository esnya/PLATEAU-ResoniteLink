using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class BuildingAttributePredicatesTests
{
    [Theory]
    [InlineData("4119", (int)PlateauBuildingUse.DetachedResidential)]
    [InlineData("403", (int)PlateauBuildingUse.Commercial)]
    [InlineData("43102", (int)PlateauBuildingUse.Warehouse)]
    [InlineData("181", (int)PlateauBuildingUse.Education)]
    public void HasUseMatchesBroadCityGmlFunctionCodes(string functionCode, int useValue)
    {
        BuildingAttributeContext attributes = BuildingAttributeContext.Empty with
        {
            CityGmlFunctionCodes = [functionCode],
        };

        Assert.True(BuildingAttributePredicates.HasUse(attributes, (PlateauBuildingUse)useValue));
    }

    [Fact]
    public void HasRawBuildingCodeMatchesFunctionAndClassBroadCodes()
    {
        BuildingAttributeContext attributes = BuildingAttributeContext.Empty with
        {
            CityGmlFunctionCodes = ["4039"],
            CityGmlClassCodes = ["4512"],
        };

        Assert.True(BuildingAttributePredicates.HasRawBuildingCode(attributes, "403"));
        Assert.True(BuildingAttributePredicates.HasRawBuildingCode(attributes, "451"));
        Assert.False(BuildingAttributePredicates.HasRawBuildingCode(attributes, "431"));
    }

    [Fact]
    public void HasNightOccupancyUsesResidentialOrCommercialNightCodes()
    {
        BuildingAttributeContext apartment = BuildingAttributeContext.Empty with
        {
            Uses = [new BuildingCodeValue<PlateauBuildingUse>(PlateauBuildingUse.Apartment, "412")],
        };
        BuildingAttributeContext nightlife = BuildingAttributeContext.Empty with
        {
            CityGmlFunctionCodes = ["403"],
        };

        Assert.True(BuildingAttributePredicates.HasNightOccupancy(apartment));
        Assert.True(BuildingAttributePredicates.HasNightOccupancy(nightlife));
    }

    [Fact]
    public void StructurePredicatesSeparateRobustAndBrickLikeStructures()
    {
        BuildingAttributeContext robust = BuildingAttributeContext.Empty with
        {
            Structures = [new BuildingCodeValue<PlateauBuildingStructure>(PlateauBuildingStructure.ReinforcedConcrete, "602")],
        };
        BuildingAttributeContext brickLike = BuildingAttributeContext.Empty with
        {
            Structures = [new BuildingCodeValue<PlateauBuildingStructure>(PlateauBuildingStructure.ConcreteBlock, "606")],
        };

        Assert.True(BuildingAttributePredicates.HasRobustStructure(robust));
        Assert.False(BuildingAttributePredicates.HasBrickLikeStructure(robust));
        Assert.False(BuildingAttributePredicates.HasRobustStructure(brickLike));
        Assert.True(BuildingAttributePredicates.HasBrickLikeStructure(brickLike));
    }
}
