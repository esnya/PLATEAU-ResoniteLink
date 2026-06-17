using System;
using System.Linq;
using System.Xml.Linq;


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
    public void ParseDropsPlateauMissingSentinelsBeforeAttributeContext()
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

        Assert.Null(attributes.MeasuredHeightMeters);
        Assert.Null(attributes.StoreysAboveGround);
        Assert.Null(attributes.StoreysBelowGround);
    }

    [Fact]
    public void ParseDropsNonMeterHeightButKeepsAreaWithoutMeterRequirement()
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

        Assert.Null(attributes.MeasuredHeightMeters);
        Assert.NotNull(attributes.BuildingFootprintArea);
        Assert.NotNull(attributes.BuildingHeight);
        BuildingMetricValue footprintArea = attributes.BuildingFootprintArea;
        BuildingMetricValue buildingHeight = attributes.BuildingHeight;
        Assert.Equal(160.5, footprintArea.Value);
        Assert.Equal(9.25, buildingHeight.Value);
    }

    [Fact]
    public void ParseDropsBlankMetricElementsBeforeAttributeContext()
    {
        XElement element = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="urn:bldg">
              <bldg:measuredHeight uom="m"> </bldg:measuredHeight>
            </bldg:Building>
            """);

        BuildingAttributeContext attributes = BuildingAttributeParser.Parse(element);

        Assert.Null(attributes.MeasuredHeightMeters);
    }

    [Fact]
    public void ParseDropsNonFiniteMetricTextBeforeAttributeContext()
    {
        XElement element = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="urn:bldg">
              <bldg:measuredHeight uom="m">Infinity</bldg:measuredHeight>
              <bldg:BuildingDetailAttribute>
                <bldg:buildingHeight uom="m">1e309</bldg:buildingHeight>
              </bldg:BuildingDetailAttribute>
            </bldg:Building>
            """);

        BuildingAttributeContext attributes = BuildingAttributeParser.Parse(element);

        Assert.Null(attributes.MeasuredHeightMeters);
        Assert.Null(attributes.BuildingHeight);
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

    [Fact]
    public void MetricValueFactoriesRejectInvalidPayloadShapes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingMetricValue(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingMetricValue(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BuildingMetricValue(-1.0));
    }
}
