using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Formats;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class CityGmlLodSelectorTests
{
    [Fact]
    public void SelectPreferredSurfaceElements_PicksHighestNonExcludedLod()
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

        CityGmlLodSelector selector = new();
        CityGmlLodSelection selection = selector.SelectPreferredSurfaceElements(
            cityObject,
            "bldg",
            isMarking: false,
            new LodFilteringStrategy(globalExcludeLodLevels: new HashSet<int> { 2 }));

        Assert.Equal(1, selection.LodLevel);
        Assert.Single(selection.SurfaceElements);
        Assert.Equal("poly-lod1", selection.SurfaceElements[0].Attribute(XName.Get("id", "http://www.opengis.net/gml"))?.Value);
    }
}
