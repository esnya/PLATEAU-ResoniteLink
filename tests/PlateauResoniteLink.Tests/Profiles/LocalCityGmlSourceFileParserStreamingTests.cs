using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class LocalCityGmlSourceFileParserStreamingTests
{
    [Fact]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_YieldsFirstObjectBeforeBlockedSecondHalfOfStream()
    {
        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0100 10</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-1">
                  <gml:name>Building One</gml:name>
                  <bldg:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-1">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-1">
                              <gml:posList>35.0000 139.0000 0 35.0000 139.0010 0 35.0010 139.0010 8 35.0000 139.0000 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod1MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-2">
                  <gml:name>Building Two</gml:name>
                  <bldg:lod1MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <gml:Polygon gml:id="poly-2">
                          <gml:exterior>
                            <gml:LinearRing gml:id="ring-2">
                              <gml:posList>35.0020 139.0020 0 35.0020 139.0030 0 35.0030 139.0030 8 35.0020 139.0020 0</gml:posList>
                            </gml:LinearRing>
                          </gml:exterior>
                        </gml:Polygon>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod1MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        int firstMemberOffset = xml.IndexOf("<core:cityObjectMember>", StringComparison.Ordinal);
        int secondMemberOffset = xml.IndexOf("<core:cityObjectMember>", firstMemberOffset + 1, StringComparison.Ordinal);
        GateableDatasetContentSource datasetSource = new(bytes, secondMemberOffset);
        SourceFileDescriptor sourceFile = new(
            "udx/bldg/53394525/streaming.gml",
            "bldg",
            "53394525",
            RequiresMeshCodeBoundsFilter: false);

        SourceFilePipeline[] pipelines = await LocalCityGmlSourceFileParser.CreateSourceFilePipelinesCoreAsync(
            [sourceFile],
            datasetSource,
            [],
            logger: NullLogger.Instance,
            new LodFilteringStrategy(),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector(),
            CancellationToken.None);

        await using IAsyncEnumerator<ParsedCityObject> enumerator =
            pipelines.Single().StreamParsedCityObjectsAsync().GetAsyncEnumerator();

        Task<bool> firstMoveTask = enumerator.MoveNextAsync().AsTask();
        Assert.Same(firstMoveTask, await Task.WhenAny(firstMoveTask, Task.Delay(TimeSpan.FromSeconds(1))));
        Assert.True(await firstMoveTask);
        Assert.Equal("Building One", enumerator.Current.DisplayName);

        Task<bool> secondMoveTask = enumerator.MoveNextAsync().AsTask();
        Assert.False(secondMoveTask.IsCompleted);

        datasetSource.Release();

        Assert.True(await secondMoveTask);
        Assert.Equal("Building Two", enumerator.Current.DisplayName);
    }

    [Fact]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_PreservesFloorMetadata()
    {
        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0" xmlns:uro="https://www.geospatial.jp/iur/uro/3.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0100 12</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-1">
                  <gml:name>Building One</gml:name>
                  <bldg:class>3001</bldg:class>
                  <bldg:function>401</bldg:function>
                  <bldg:usage>411</bldg:usage>
                  <bldg:roofType>5</bldg:roofType>
                  <bldg:measuredHeight uom="m">11.8</bldg:measuredHeight>
                  <bldg:storeysAboveGround>4</bldg:storeysAboveGround>
                  <bldg:storeysBelowGround>9999</bldg:storeysBelowGround>
                  <uro:buildingDetailAttribute>
                    <uro:BuildingDetailAttribute>
                      <uro:buildingStructureType>601</uro:buildingStructureType>
                      <uro:buildingFootprintArea>120.5</uro:buildingFootprintArea>
                      <uro:buildingRoofEdgeArea>-9999</uro:buildingRoofEdgeArea>
                      <uro:buildingHeight>11.8</uro:buildingHeight>
                      <uro:eaveHeight>9.7</uro:eaveHeight>
                      <uro:detailedUsage>1110</uro:detailedUsage>
                    </uro:BuildingDetailAttribute>
                  </uro:buildingDetailAttribute>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <bldg:WallSurface>
                          <gml:Polygon gml:id="poly-1">
                            <gml:exterior>
                              <gml:LinearRing gml:id="ring-1">
                                <gml:posList>35.0000 139.0000 0 35.0000 139.0010 0 35.0000 139.0010 12 35.0000 139.0000 12 35.0000 139.0000 0</gml:posList>
                              </gml:LinearRing>
                            </gml:exterior>
                          </gml:Polygon>
                        </bldg:WallSurface>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        InMemoryDatasetContentSource datasetSource = new(Encoding.UTF8.GetBytes(xml));
        ParsedCityObject parsedCityObject = await ParseSingleBuildingAsync(datasetSource);

        Assert.Equal(4, parsedCityObject.FloorsAboveGround);
        Assert.NotNull(parsedCityObject.MeasuredHeightMeters);
        Assert.InRange(parsedCityObject.MeasuredHeightMeters!.Value, 11.799999, 11.800001);

        BuildingAttributeContext attributes = parsedCityObject.BuildingAttributes;
        Assert.NotNull(attributes.RoofShape);
        Assert.Equal(CityGmlRoofShape.Shed, attributes.RoofShape!.Value);
        Assert.Equal("5", attributes.RoofShape.Code);
        Assert.Contains(attributes.Uses, value => value.Value == PlateauBuildingUse.DetachedResidential && value.Code == "411");
        Assert.Contains(attributes.DetailedUses, value => value.Value == PlateauBuildingUse.DetachedResidential && value.Code == "1110");
        Assert.Contains(attributes.Structures, value => value.Value == PlateauBuildingStructure.Wood && value.Code == "601");
        Assert.Equal(["3001"], attributes.CityGmlClassCodes);
        Assert.Equal(["401"], attributes.CityGmlFunctionCodes);
        Assert.NotNull(attributes.MeasuredHeightMeters);
        Assert.NotNull(attributes.StoreysAboveGround);
        Assert.Null(attributes.StoreysBelowGround);
        Assert.NotNull(attributes.BuildingFootprintArea);
        Assert.Null(attributes.BuildingRoofEdgeArea);
        Assert.NotNull(attributes.BuildingHeight);
        Assert.NotNull(attributes.EaveHeight);
        BuildingMetricValue footprintArea = attributes.BuildingFootprintArea;
        BuildingMetricValue eaveHeight = attributes.EaveHeight;
        Assert.InRange(footprintArea.Value, 120.499999, 120.500001);
        Assert.InRange(eaveHeight.Value, 9.699999, 9.700001);
    }

    [Fact]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_SkipsInvalidInteriorRingsBeforeSurfaceConstruction()
    {
        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0100 12</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-1">
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <bldg:WallSurface>
                          <gml:Polygon gml:id="poly-1">
                            <gml:exterior>
                              <gml:LinearRing gml:id="ring-1">
                                <gml:posList>35.0000 139.0000 0 35.0000 139.0010 0 35.0000 139.0010 12 35.0000 139.0000 12 35.0000 139.0000 0</gml:posList>
                              </gml:LinearRing>
                            </gml:exterior>
                            <gml:interior>
                              <gml:LinearRing gml:id="invalid-interior">
                                <gml:posList>35.0002 139.0002 1 35.0002 139.0004 1</gml:posList>
                              </gml:LinearRing>
                            </gml:interior>
                            <gml:interior />
                          </gml:Polygon>
                        </bldg:WallSurface>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        InMemoryDatasetContentSource datasetSource = new(Encoding.UTF8.GetBytes(xml));

        ParsedCityObject parsedCityObject = await ParseSingleBuildingAsync(datasetSource);
        ParsedSurface surface = Assert.Single(parsedCityObject.Surfaces);

        Assert.Empty(surface.InteriorRings);
    }

    [Fact]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_SkipsPolygonWithInvalidExteriorRing()
    {
        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0100 12</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-1">
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <bldg:WallSurface>
                          <gml:Polygon gml:id="poly-1">
                            <gml:exterior>
                              <gml:LinearRing gml:id="invalid-exterior">
                                <gml:posList>35.0000 139.0000 0 35.0000 139.0010 0</gml:posList>
                              </gml:LinearRing>
                            </gml:exterior>
                          </gml:Polygon>
                        </bldg:WallSurface>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        InMemoryDatasetContentSource datasetSource = new(Encoding.UTF8.GetBytes(xml));

        ParsedCityObject? parsedCityObject = await TryParseSingleBuildingAsync(datasetSource);

        Assert.Null(parsedCityObject);
    }

    private static async Task<ParsedCityObject> ParseSingleBuildingWithDetailAttributeAsync(string detailAttributeXml)
    {
        string xml =
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0" xmlns:uro="https://www.geospatial.jp/iur/uro/3.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0100 12</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-1">
                  <uro:buildingDetailAttribute>
                    <uro:BuildingDetailAttribute>
                      {{detailAttributeXml}}
                    </uro:BuildingDetailAttribute>
                  </uro:buildingDetailAttribute>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <bldg:WallSurface>
                          <gml:Polygon gml:id="poly-1">
                            <gml:exterior>
                              <gml:LinearRing gml:id="ring-1">
                                <gml:posList>35.0000 139.0000 0 35.0000 139.0010 0 35.0000 139.0010 12 35.0000 139.0000 12 35.0000 139.0000 0</gml:posList>
                              </gml:LinearRing>
                            </gml:exterior>
                          </gml:Polygon>
                        </bldg:WallSurface>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        InMemoryDatasetContentSource datasetSource = new(Encoding.UTF8.GetBytes(xml));
        return await ParseSingleBuildingAsync(datasetSource);
    }

    private static async Task<ParsedCityObject> ParseSingleBuildingAsync(InMemoryDatasetContentSource datasetSource)
    {
        ParsedCityObject? parsedCityObject = await TryParseSingleBuildingAsync(datasetSource);

        Assert.NotNull(parsedCityObject);
        return parsedCityObject;
    }

    private static async Task<ParsedCityObject?> TryParseSingleBuildingAsync(InMemoryDatasetContentSource datasetSource)
    {
        SourceFileDescriptor sourceFile = new(
            "udx/bldg/53394525/metadata.gml",
            "bldg",
            "53394525",
            RequiresMeshCodeBoundsFilter: false);

        SourceFilePipeline[] pipelines = await LocalCityGmlSourceFileParser.CreateSourceFilePipelinesCoreAsync(
            [sourceFile],
            datasetSource,
            [],
            logger: NullLogger.Instance,
            new LodFilteringStrategy(),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector(),
            CancellationToken.None);

        ParsedCityObject? parsedCityObject = null;
        await foreach (ParsedCityObject cityObject in pipelines.Single().StreamParsedCityObjectsAsync())
        {
            parsedCityObject = cityObject;
            break;
        }

        return parsedCityObject;
    }

    [Theory]
    [InlineData("7", (int)CityGmlRoofShape.Irimoya)]
    [InlineData("9", (int)CityGmlRoofShape.Mansard)]
    [InlineData("14", (int)CityGmlRoofShape.Sawtooth)]
    [InlineData("21", (int)CityGmlRoofShape.Gambrel)]
    [InlineData("23", (int)CityGmlRoofShape.Arch)]
    [InlineData("24", (int)CityGmlRoofShape.Dome)]
    [InlineData("28", (int)CityGmlRoofShape.Other)]
    [InlineData("9020", (int)CityGmlRoofShape.Unknown)]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_MapsPlateauRoofTypeCodes(
        string roofTypeCode,
        int expectedShapeValue)
    {
        CityGmlRoofShape expectedShape = (CityGmlRoofShape)expectedShapeValue;
        ParsedCityObject parsedCityObject = await ParseSingleBuildingWithRoofTypeAsync(roofTypeCode);

        BuildingCodeValue<CityGmlRoofShape>? roofShape = parsedCityObject.BuildingAttributes.RoofShape;
        Assert.NotNull(roofShape);
        Assert.Equal(expectedShape, roofShape!.Value);
        Assert.Equal(roofTypeCode, roofShape.Code);
    }

    [Theory]
    [InlineData("602", (int)PlateauBuildingStructure.SteelReinforcedConcrete)]
    [InlineData("603", (int)PlateauBuildingStructure.ReinforcedConcrete)]
    [InlineData("604", (int)PlateauBuildingStructure.Steel)]
    [InlineData("605", (int)PlateauBuildingStructure.LightweightSteel)]
    [InlineData("606", (int)PlateauBuildingStructure.ConcreteBlock)]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_MapsPlateauBuildingStructureCodes(
        string structureCode,
        int expectedStructureValue)
    {
        ParsedCityObject parsedCityObject = await ParseSingleBuildingWithDetailAttributeAsync(
            $"<uro:buildingStructureType>{structureCode}</uro:buildingStructureType>");
        PlateauBuildingStructure expectedStructure = (PlateauBuildingStructure)expectedStructureValue;

        BuildingCodeValue<PlateauBuildingStructure> structure = Assert.Single(parsedCityObject.BuildingAttributes.Structures);
        Assert.Equal(expectedStructure, structure.Value);
        Assert.Equal(structureCode, structure.Code);
    }

    [Theory]
    [InlineData("412101", (int)PlateauBuildingUse.Apartment)]
    [InlineData("441101", (int)PlateauBuildingUse.Factory)]
    [InlineData("422101", (int)PlateauBuildingUse.Education)]
    [InlineData("402101", (int)PlateauBuildingUse.Commercial)]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_MapsPlateauDetailedUsageCodes(
        string detailedUsageCode,
        int expectedUseValue)
    {
        ParsedCityObject parsedCityObject = await ParseSingleBuildingWithDetailAttributeAsync(
            $"<uro:detailedUsage>{detailedUsageCode}</uro:detailedUsage>");
        PlateauBuildingUse expectedUse = (PlateauBuildingUse)expectedUseValue;

        BuildingCodeValue<PlateauBuildingUse> detailedUse = Assert.Single(parsedCityObject.BuildingAttributes.DetailedUses);
        Assert.Equal(expectedUse, detailedUse.Value);
        Assert.Equal(detailedUsageCode, detailedUse.Code);
    }

    [Fact]
    public async Task SourceFilePipeline_StreamParsedCityObjectsAsync_IgnoresMeasuredHeightWithNonMeterUnit()
    {
        string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0100 12</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-1">
                  <gml:name>Building One</gml:name>
                  <bldg:measuredHeight uom="ft">11.8</bldg:measuredHeight>
                  <bldg:storeysAboveGround>4</bldg:storeysAboveGround>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <bldg:WallSurface>
                          <gml:Polygon gml:id="poly-1">
                            <gml:exterior>
                              <gml:LinearRing gml:id="ring-1">
                                <gml:posList>35.0000 139.0000 0 35.0000 139.0010 0 35.0000 139.0010 12 35.0000 139.0000 12 35.0000 139.0000 0</gml:posList>
                              </gml:LinearRing>
                            </gml:exterior>
                          </gml:Polygon>
                        </bldg:WallSurface>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        InMemoryDatasetContentSource datasetSource = new(Encoding.UTF8.GetBytes(xml));
        SourceFileDescriptor sourceFile = new(
            "udx/bldg/53394525/metadata.gml",
            "bldg",
            "53394525",
            RequiresMeshCodeBoundsFilter: false);

        SourceFilePipeline[] pipelines = await LocalCityGmlSourceFileParser.CreateSourceFilePipelinesCoreAsync(
            [sourceFile],
            datasetSource,
            [],
            logger: NullLogger.Instance,
            new LodFilteringStrategy(),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector(),
            CancellationToken.None);

        ParsedCityObject? parsedCityObject = null;
        await foreach (ParsedCityObject cityObject in pipelines.Single().StreamParsedCityObjectsAsync())
        {
            parsedCityObject = cityObject;
            break;
        }

        Assert.NotNull(parsedCityObject);
        Assert.Equal(4, parsedCityObject!.FloorsAboveGround);
        Assert.Null(parsedCityObject.MeasuredHeightMeters);
    }

    private static async Task<ParsedCityObject> ParseSingleBuildingWithRoofTypeAsync(string roofTypeCode)
    {
        string xml =
            $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:gml="http://www.opengis.net/gml" xmlns:bldg="http://www.opengis.net/citygml/building/2.0">
              <gml:boundedBy>
                <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
                  <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
                  <gml:upperCorner>35.0100 139.0100 12</gml:upperCorner>
                </gml:Envelope>
              </gml:boundedBy>
              <core:cityObjectMember>
                <bldg:Building gml:id="bldg-1">
                  <bldg:roofType>{{roofTypeCode}}</bldg:roofType>
                  <bldg:lod2MultiSurface>
                    <gml:MultiSurface>
                      <gml:surfaceMember>
                        <bldg:WallSurface>
                          <gml:Polygon gml:id="poly-1">
                            <gml:exterior>
                              <gml:LinearRing gml:id="ring-1">
                                <gml:posList>35.0000 139.0000 0 35.0000 139.0010 0 35.0000 139.0010 12 35.0000 139.0000 12 35.0000 139.0000 0</gml:posList>
                              </gml:LinearRing>
                            </gml:exterior>
                          </gml:Polygon>
                        </bldg:WallSurface>
                      </gml:surfaceMember>
                    </gml:MultiSurface>
                  </bldg:lod2MultiSurface>
                </bldg:Building>
              </core:cityObjectMember>
            </core:CityModel>
            """;
        InMemoryDatasetContentSource datasetSource = new(Encoding.UTF8.GetBytes(xml));
        SourceFileDescriptor sourceFile = new(
            "udx/bldg/53394525/metadata.gml",
            "bldg",
            "53394525",
            RequiresMeshCodeBoundsFilter: false);

        SourceFilePipeline[] pipelines = await LocalCityGmlSourceFileParser.CreateSourceFilePipelinesCoreAsync(
            [sourceFile],
            datasetSource,
            [],
            logger: NullLogger.Instance,
            new LodFilteringStrategy(),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector(),
            CancellationToken.None);

        await foreach (ParsedCityObject cityObject in pipelines.Single().StreamParsedCityObjectsAsync())
        {
            return cityObject;
        }

        throw new InvalidOperationException("No city object was parsed.");
    }
    private sealed class GateableDatasetContentSource(byte[] payload, int gateOffset) : IPlateauDatasetContentSource
    {
        private readonly TaskCompletionSource releaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string SourcePath => "/tmp/streaming";

        public IReadOnlyList<string> EnumerateFiles()
        {
            return ["udx/bldg/53394525/streaming.gml"];
        }

        public bool FileExists(string relativePath)
        {
            return true;
        }

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to the caller as a Stream result.")]
        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            Stream stream = new GateableReadStream(payload, gateOffset, releaseSignal.Task);
            return ValueTask.FromResult(stream);
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Release()
        {
            releaseSignal.TrySetResult();
        }
    }

    private sealed class InMemoryDatasetContentSource(byte[] payload) : IPlateauDatasetContentSource
    {
        public string SourcePath => "/tmp/streaming";

        public IReadOnlyList<string> EnumerateFiles()
        {
            return ["udx/bldg/53394525/metadata.gml"];
        }

        public bool FileExists(string relativePath)
        {
            return true;
        }

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return null;
        }

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to the caller as a Stream result.")]
        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<Stream>(new MemoryStream(payload, writable: false));
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class GateableReadStream(
        byte[] payload,
        int gateOffset,
        Task releaseTask)
        : Stream
    {
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => payload.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (position >= payload.Length)
            {
                return 0;
            }

            if (position >= gateOffset && !releaseTask.IsCompleted)
            {
                await releaseTask.WaitAsync(cancellationToken);
            }

            int availableCount = payload.Length - position;
            if (position < gateOffset && !releaseTask.IsCompleted)
            {
                availableCount = Math.Min(availableCount, gateOffset - position);
            }

            int bytesToCopy = Math.Min(buffer.Length, availableCount);
            payload.AsMemory(position, bytesToCopy).CopyTo(buffer);
            position += bytesToCopy;
            return bytesToCopy;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
