using System;
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

        Assert.IsType<BuildingMetricValue.MissingMetricValue>(attributes.MeasuredHeightMeters);
        Assert.IsType<BuildingMetricValue.MissingMetricValue>(attributes.StoreysAboveGround);
        Assert.IsType<BuildingMetricValue.MissingMetricValue>(attributes.StoreysBelowGround);
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

        BuildingMetricValue.InvalidMetricValue invalidMeasuredHeight =
            Assert.IsType<BuildingMetricValue.InvalidMetricValue>(attributes.MeasuredHeightMeters);
        BuildingMetricValue.KnownMetricValue footprintArea =
            Assert.IsType<BuildingMetricValue.KnownMetricValue>(attributes.BuildingFootprintArea);
        BuildingMetricValue.KnownMetricValue buildingHeight =
            Assert.IsType<BuildingMetricValue.KnownMetricValue>(attributes.BuildingHeight);
        Assert.Equal("12", invalidMeasuredHeight.Raw);
        Assert.Equal(160.5, footprintArea.Value);
        Assert.Equal(9.25, buildingHeight.Value);
    }

    [Fact]
    public void ParsePreservesBlankMetricElementsAsInvalidMetrics()
    {
        XElement element = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="urn:bldg">
              <bldg:measuredHeight uom="m"> </bldg:measuredHeight>
            </bldg:Building>
            """);

        BuildingAttributeContext attributes = BuildingAttributeParser.Parse(element);

        BuildingMetricValue.InvalidMetricValue invalidMeasuredHeight =
            Assert.IsType<BuildingMetricValue.InvalidMetricValue>(attributes.MeasuredHeightMeters);
        Assert.Equal(string.Empty, invalidMeasuredHeight.Raw);
    }

    [Fact]
    public void ParsePreservesNonFiniteMetricTextAsInvalidMetrics()
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

        BuildingMetricValue.InvalidMetricValue invalidMeasuredHeight =
            Assert.IsType<BuildingMetricValue.InvalidMetricValue>(attributes.MeasuredHeightMeters);
        BuildingMetricValue.InvalidMetricValue invalidBuildingHeight =
            Assert.IsType<BuildingMetricValue.InvalidMetricValue>(attributes.BuildingHeight);
        Assert.Equal("Infinity", invalidMeasuredHeight.Raw);
        Assert.Equal("1e309", invalidBuildingHeight.Raw);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildingMetricValue.Known(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildingMetricValue.Known(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildingMetricValue.Known(-1.0));
        Assert.Throws<ArgumentNullException>(() => BuildingMetricValue.Invalid(null!));
    }
}
