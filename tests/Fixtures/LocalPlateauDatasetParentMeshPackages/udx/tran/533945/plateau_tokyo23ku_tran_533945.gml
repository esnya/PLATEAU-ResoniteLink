<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:gml="http://www.opengis.net/gml"
  xmlns:tran="http://www.opengis.net/citygml/transportation/2.0">
  <gml:boundedBy>
    <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
      <gml:lowerCorner>35.6834 139.6876 0</gml:lowerCorner>
      <gml:upperCorner>35.7006 139.7106 2</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <core:cityObjectMember>
    <tran:Road gml:id="tran-parent-1">
      <gml:name>Parent Tile Road</gml:name>
      <tran:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-parent-tran-1">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-parent-tran-1">
                  <gml:posList>35.6837 139.6879 0 35.6839 139.6879 0 35.6839 139.6882 0 35.6837 139.6882 0 35.6837 139.6879 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </tran:lod1MultiSurface>
    </tran:Road>
  </core:cityObjectMember>
  <core:cityObjectMember>
    <tran:Road gml:id="tran-parent-outside">
      <gml:name>Outside Road</gml:name>
      <tran:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-parent-tran-outside">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-parent-tran-outside">
                  <gml:posList>35.7001 139.7101 0 35.7003 139.7101 0 35.7003 139.7103 0 35.7001 139.7103 0 35.7001 139.7101 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </tran:lod1MultiSurface>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>
