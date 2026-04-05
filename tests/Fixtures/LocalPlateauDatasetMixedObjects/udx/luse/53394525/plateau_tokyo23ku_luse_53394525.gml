<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:gml="http://www.opengis.net/gml"
  xmlns:luse="http://www.opengis.net/citygml/landuse/2.0">
  <gml:boundedBy>
    <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6668" srsDimension="3">
      <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
      <gml:upperCorner>35.0006 139.0006 0</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <core:cityObjectMember>
    <luse:LandUse gml:id="luse-1">
      <gml:name>Land Use One</gml:name>
      <luse:lod1MultiSurface>
        <gml:MultiSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6668" srsDimension="3">
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-luse-1">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-luse-1">
                  <gml:posList>35.0000 139.0000 0 35.0003 139.0000 0 35.0003 139.0003 0 35.0000 139.0003 0 35.0000 139.0000 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </luse:lod1MultiSurface>
    </luse:LandUse>
  </core:cityObjectMember>
</core:CityModel>
