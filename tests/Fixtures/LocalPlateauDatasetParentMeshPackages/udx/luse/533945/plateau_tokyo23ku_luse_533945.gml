<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:gml="http://www.opengis.net/gml"
  xmlns:luse="http://www.opengis.net/citygml/landuse/2.0">
  <gml:boundedBy>
    <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6668" srsDimension="3">
      <gml:lowerCorner>35.6834 139.6876 0</gml:lowerCorner>
      <gml:upperCorner>35.6842 139.6884 0</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <core:cityObjectMember>
    <luse:LandUse gml:id="luse-parent-1">
      <gml:name>Parent Tile Land Use</gml:name>
      <luse:lod1MultiSurface>
        <gml:MultiSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6668" srsDimension="3">
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-parent-luse-1">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-parent-luse-1">
                  <gml:posList>35.6838 139.6880 0 35.6840 139.6880 0 35.6840 139.6882 0 35.6838 139.6882 0 35.6838 139.6880 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </luse:lod1MultiSurface>
    </luse:LandUse>
  </core:cityObjectMember>
</core:CityModel>
