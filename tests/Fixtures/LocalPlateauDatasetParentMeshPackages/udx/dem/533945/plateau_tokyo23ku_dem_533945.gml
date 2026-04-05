<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:dem="http://www.opengis.net/citygml/relief/2.0"
  xmlns:gml="http://www.opengis.net/gml">
  <gml:boundedBy>
    <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
      <gml:lowerCorner>35.6834 139.6876 2</gml:lowerCorner>
      <gml:upperCorner>35.6842 139.6884 6</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <core:cityObjectMember>
    <dem:ReliefFeature gml:id="dem-parent-1">
      <gml:name>Parent Tile Relief</gml:name>
      <dem:reliefComponent>
        <dem:TINRelief gml:id="dem-parent-component-1">
          <dem:tin>
            <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
              <gml:trianglePatches>
                <gml:Triangle gml:id="tri-parent-dem-1">
                  <gml:exterior>
                    <gml:LinearRing gml:id="ring-parent-dem-1">
                      <gml:posList>35.6838 139.6880 2 35.6841 139.6880 4 35.6839 139.6883 6 35.6838 139.6880 2</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Triangle>
              </gml:trianglePatches>
            </gml:TriangulatedSurface>
          </dem:tin>
        </dem:TINRelief>
      </dem:reliefComponent>
    </dem:ReliefFeature>
  </core:cityObjectMember>
</core:CityModel>
