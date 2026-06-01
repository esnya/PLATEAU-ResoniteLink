using System.Linq;
using System.Xml.Linq;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class BuildingAttributeParserTests
{
    [Fact]
    public void ParseMapsDirectAndDescendantBuildingCodes()
    {
        XElement element = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="urn:bldg">
              <bldg:class>3001</bldg:class>
              <bldg:function>401</bldg:function>
              <bldg:usage>411</bldg:usage>
              <bldg:roofType>14</bldg:roofType>
              <bldg:attributes>
                <bldg:detailedUsage>441101</bldg:detailedUsage>
                <bldg:buildingStructureType>603</bldg:buildingStructureType>
              </bldg:attributes>
            </bldg:Building>
            """);

        BuildingAttributeContext attributes = BuildingAttributeParser.Parse(element);

        Assert.Equal(CityGmlRoofShape.Sawtooth, attributes.RoofShape?.Value);
        Assert.Equal("14", attributes.RoofShape?.Code);
        Assert.Equal(PlateauBuildingUse.DetachedResidential, Assert.Single(attributes.Uses).Value);
        Assert.Equal(PlateauBuildingUse.Factory, Assert.Single(attributes.DetailedUses).Value);
        Assert.Equal(PlateauBuildingStructure.ReinforcedConcrete, Assert.Single(attributes.Structures).Value);
        Assert.Equal("3001", Assert.Single(attributes.CityGmlClassCodes));
        Assert.Equal("401", Assert.Single(attributes.CityGmlFunctionCodes));
    }

    [Fact]
    public void ParseTreatsPlateauMissingSentinelsAsMissingMetrics()
    {
        XElement element = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="urn:bldg">
              <bldg:measuredHeight uom="m">-9999</bldg:measuredHeight>
              <bldg:storeysAboveGround>9999</bldg:storeysAboveGround>
              <bldg:storeysBelowGround>0001</bldg:storeysBelowGround>
            </bldg:Building>
            """);

        BuildingAttributeContext attributes = BuildingAttributeParser.Parse(element);

        Assert.IsType<MissingBuildingMetricValue>(attributes.MeasuredHeightMeters);
        Assert.IsType<MissingBuildingMetricValue>(attributes.StoreysAboveGround);
        Assert.IsType<MissingBuildingMetricValue>(attributes.StoreysBelowGround);
    }

    [Fact]
    public void ParseTreatsBlankMetricElementsAsMissingMetrics()
    {
        XElement element = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="urn:bldg">
              <bldg:measuredHeight uom="m"> </bldg:measuredHeight>
              <bldg:storeysAboveGround />
              <bldg:BuildingDetailAttribute>
                <bldg:buildingFootprintArea uom="m2"></bldg:buildingFootprintArea>
              </bldg:BuildingDetailAttribute>
            </bldg:Building>
            """);

        BuildingAttributeContext attributes = BuildingAttributeParser.Parse(element);

        Assert.IsType<MissingBuildingMetricValue>(attributes.MeasuredHeightMeters);
        Assert.IsType<MissingBuildingMetricValue>(attributes.StoreysAboveGround);
        Assert.IsType<MissingBuildingMetricValue>(attributes.BuildingFootprintArea);
    }

    [Fact]
    public void ParseRejectsNonMeterHeightButKeepsAreaWithoutMeterRequirement()
    {
        XElement element = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="urn:bldg">
              <bldg:measuredHeight uom="ft">12</bldg:measuredHeight>
              <bldg:BuildingDetailAttribute>
                <bldg:buildingFootprintArea uom="m2">160.5</bldg:buildingFootprintArea>
                <bldg:buildingHeight uom="m">9.25</bldg:buildingHeight>
              </bldg:BuildingDetailAttribute>
            </bldg:Building>
            """);

        BuildingAttributeContext attributes = BuildingAttributeParser.Parse(element);

        InvalidBuildingMetricValue measuredHeight = Assert.IsType<InvalidBuildingMetricValue>(attributes.MeasuredHeightMeters);
        Assert.Equal("12", measuredHeight.Raw);
        KnownBuildingMetricValue footprintArea = Assert.IsType<KnownBuildingMetricValue>(attributes.BuildingFootprintArea);
        Assert.Equal(160.5, footprintArea.Value);
        KnownBuildingMetricValue buildingHeight = Assert.IsType<KnownBuildingMetricValue>(attributes.BuildingHeight);
        Assert.Equal(9.25, buildingHeight.Value);
    }

    [Fact]
    public void ParseDropsBlankDirectCodeValues()
    {
        XElement element = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="urn:bldg">
              <bldg:function> </bldg:function>
              <bldg:function>402101</bldg:function>
            </bldg:Building>
            """);

        BuildingAttributeContext attributes = BuildingAttributeParser.Parse(element);

        Assert.Equal(["402101"], attributes.CityGmlFunctionCodes.ToArray());
    }
}
