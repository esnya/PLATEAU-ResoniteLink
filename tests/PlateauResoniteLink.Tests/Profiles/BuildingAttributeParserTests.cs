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

        Assert.Equal(BuildingMetricValueKind.Missing, attributes.MeasuredHeightMeters.Kind);
        Assert.Equal(BuildingMetricValueKind.Missing, attributes.StoreysAboveGround.Kind);
        Assert.Equal(BuildingMetricValueKind.Missing, attributes.StoreysBelowGround.Kind);
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

        Assert.Equal(BuildingMetricValueKind.Invalid, attributes.MeasuredHeightMeters.Kind);
        Assert.Equal("12", attributes.MeasuredHeightMeters.Raw);
        Assert.Equal(BuildingMetricValueKind.Known, attributes.BuildingFootprintArea.Kind);
        Assert.Equal(160.5, attributes.BuildingFootprintArea.Value);
        Assert.Equal(BuildingMetricValueKind.Known, attributes.BuildingHeight.Kind);
        Assert.Equal(9.25, attributes.BuildingHeight.Value);
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
