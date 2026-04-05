<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:gml="http://www.opengis.net/gml">
  <gml:boundedBy>
    <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
      <gml:lowerCorner>35.6834 139.6876 0</gml:lowerCorner>
      <gml:upperCorner>35.6842 139.6884 12</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <core:cityObjectMember>
    <bldg:Building gml:id="bldg-parent-1">
      <gml:name>Parent Tile Building</gml:name>
      <bldg:lod2MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-parent-bldg-1">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-parent-bldg-1">
                  <gml:posList>35.6836 139.6878 10 35.6839 139.6878 10 35.6839 139.6881 10 35.6836 139.6881 10 35.6836 139.6878 10</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod2MultiSurface>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>
