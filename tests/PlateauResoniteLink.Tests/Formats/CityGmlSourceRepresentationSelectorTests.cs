using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Formats;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class CityGmlSourceRepresentationSelectorTests
{
    [Fact]
    public void SelectSurfaceRepresentations_ReturnsNonExcludedSourceRepresentations()
    {
        XElement cityObject = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="http://www.opengis.net/citygml/building/2.0" xmlns:gml="http://www.opengis.net/gml">
              <bldg:lod1MultiSurface>
                <gml:MultiSurface>
                  <gml:surfaceMember>
                    <gml:Polygon gml:id="poly-lod1" />
                  </gml:surfaceMember>
                </gml:MultiSurface>
              </bldg:lod1MultiSurface>
              <bldg:lod2MultiSurface>
                <gml:MultiSurface>
                  <gml:surfaceMember>
                    <gml:Polygon gml:id="poly-lod2" />
                  </gml:surfaceMember>
                </gml:MultiSurface>
              </bldg:lod2MultiSurface>
            </bldg:Building>
            """);

        CityGmlSourceRepresentationSelector selector = new();
        CityGmlSourceRepresentationSelection[] selections = selector.SelectSurfaceRepresentations(
            cityObject,
            "bldg",
            isMarking: false,
            new LodFilteringStrategy(globalExcludeLodLevels: new HashSet<int> { 2 }));

        CityGmlSourceRepresentationSelection selection = Assert.Single(selections);
        Assert.Equal(DetailEntry.FromSourceRepresentationIndex(1), selection.DetailEntry);
        Assert.Single(selection.SurfaceElements);
        Assert.Equal("poly-lod1", selection.SurfaceElements[0].Attribute(XName.Get("id", "http://www.opengis.net/gml"))?.Value);
    }

    [Fact]
    public void SelectSurfaceRepresentations_DoesNotOutputProjectUnsupportedZeroSourceRepresentation()
    {
        XElement cityObject = XElement.Parse(
            """
            <bldg:Building xmlns:bldg="http://www.opengis.net/citygml/building/2.0" xmlns:gml="http://www.opengis.net/gml">
              <bldg:lod0RoofEdge>
                <gml:MultiCurve>
                  <gml:curveMember>
                    <gml:Polygon gml:id="poly-source-representation-0" />
                  </gml:curveMember>
                </gml:MultiCurve>
              </bldg:lod0RoofEdge>
              <bldg:lod1MultiSurface>
                <gml:MultiSurface>
                  <gml:surfaceMember>
                    <gml:Polygon gml:id="poly-source-representation-1" />
                  </gml:surfaceMember>
                </gml:MultiSurface>
              </bldg:lod1MultiSurface>
            </bldg:Building>
            """);

        CityGmlSourceRepresentationSelector selector = new();
        CityGmlSourceRepresentationSelection[] selections = selector.SelectSurfaceRepresentations(
            cityObject,
            "bldg",
            isMarking: false,
            new LodFilteringStrategy());

        CityGmlSourceRepresentationSelection selection = Assert.Single(selections);
        Assert.Equal(DetailEntry.FromSourceRepresentationIndex(1), selection.DetailEntry);
        Assert.Equal("poly-source-representation-1", selection.SurfaceElements[0].Attribute(XName.Get("id", "http://www.opengis.net/gml"))?.Value);
    }
}
