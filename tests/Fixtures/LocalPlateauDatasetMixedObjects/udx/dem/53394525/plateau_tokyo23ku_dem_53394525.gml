<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:dem="http://www.opengis.net/citygml/relief/2.0"
  xmlns:gml="http://www.opengis.net/gml">
  <gml:boundedBy>
    <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
      <gml:lowerCorner>35.0010 139.0010 5</gml:lowerCorner>
      <gml:upperCorner>35.0015 139.0015 7</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <core:cityObjectMember>
    <dem:ReliefFeature gml:id="dem-1">
      <gml:name>Relief One</gml:name>
      <dem:reliefComponent>
        <dem:TINRelief gml:id="dem-component-1">
          <dem:tin>
            <gml:TriangulatedSurface srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
              <gml:trianglePatches>
                <gml:Triangle gml:id="tri-dem-1">
                  <gml:exterior>
                    <gml:LinearRing gml:id="ring-dem-1">
                      <gml:posList>35.0010 139.0010 5 35.0014 139.0010 6 35.0012 139.0014 7 35.0010 139.0010 5</gml:posList>
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
