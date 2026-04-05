<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:app="http://www.opengis.net/citygml/appearance/2.0"
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:gml="http://www.opengis.net/gml"
  xmlns:tran="http://www.opengis.net/citygml/transportation/2.0">
  <gml:boundedBy>
    <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
      <gml:lowerCorner>35.0000 139.0000 -1</gml:lowerCorner>
      <gml:upperCorner>35.0006 139.0006 1</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <app:appearanceMember>
    <app:Appearance>
      <app:surfaceDataMember>
        <app:ParameterizedTexture>
          <app:imageURI>appearance/road.png</app:imageURI>
          <app:target uri="#poly-road-deck">
            <app:TexCoordList>
              <app:textureCoordinates ring="#ring-road-deck">0 0 2 0 2 1 0 1 0 0</app:textureCoordinates>
            </app:TexCoordList>
          </app:target>
        </app:ParameterizedTexture>
      </app:surfaceDataMember>
      <app:surfaceDataMember>
        <app:X3DMaterial>
          <app:diffuseColor>0.30 0.32 0.34</app:diffuseColor>
          <app:target uri="#poly-road-deck" />
          <app:target uri="#poly-road-side" />
        </app:X3DMaterial>
      </app:surfaceDataMember>
    </app:Appearance>
  </app:appearanceMember>
  <core:cityObjectMember>
    <tran:Road gml:id="tran-road-1">
      <gml:name>Road Segment One</gml:name>
      <tran:lod3MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-road-deck">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-road-deck">
                  <gml:posList>40 0 0 52 0 0 52 6 0 40 6 0 40 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-road-side">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-road-side">
                  <gml:posList>40 0 0 52 0 0 52 0 -1 40 0 -1 40 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </tran:lod3MultiSurface>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>
