<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:app="http://www.opengis.net/citygml/appearance/2.0"
  xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:gml="http://www.opengis.net/gml">
  <gml:boundedBy>
    <gml:Envelope srsName="http://www.opengis.net/def/crs/EPSG/0/6697" srsDimension="3">
      <gml:lowerCorner>35.0000 139.0000 0</gml:lowerCorner>
      <gml:upperCorner>35.0006 139.0006 10</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <app:appearanceMember>
    <app:Appearance>
      <app:surfaceDataMember>
        <app:ParameterizedTexture>
          <app:imageURI>appearance/roof.png</app:imageURI>
          <app:target uri="#poly-roof-1">
            <app:TexCoordList>
              <app:textureCoordinates ring="#ring-roof-1">0 0 1 0 1 1 0 1 0 0</app:textureCoordinates>
            </app:TexCoordList>
          </app:target>
          <app:target uri="#poly-roof-2">
            <app:TexCoordList>
              <app:textureCoordinates ring="#ring-roof-2">0 0 1 0 1 1 0 1 0 0</app:textureCoordinates>
            </app:TexCoordList>
          </app:target>
        </app:ParameterizedTexture>
      </app:surfaceDataMember>
      <app:surfaceDataMember>
        <app:X3DMaterial>
          <app:diffuseColor>0.92 0.92 0.92</app:diffuseColor>
          <app:target uri="#poly-roof-1" />
          <app:target uri="#poly-roof-2" />
        </app:X3DMaterial>
      </app:surfaceDataMember>
      <app:surfaceDataMember>
        <app:X3DMaterial>
          <app:diffuseColor>0.70 0.74 0.78</app:diffuseColor>
          <app:target uri="#poly-wall-1" />
        </app:X3DMaterial>
      </app:surfaceDataMember>
    </app:Appearance>
  </app:appearanceMember>
  <core:cityObjectMember>
    <bldg:Building gml:id="bldg-1">
      <gml:name>Building One</gml:name>
      <bldg:lod2MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-roof-1">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-roof-1">
                  <gml:posList>0 0 10 10 0 10 10 10 10 0 10 10 0 0 10</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-wall-1">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-wall-1">
                  <gml:posList>0 0 0 10 0 0 10 0 10 0 0 10 0 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod2MultiSurface>
    </bldg:Building>
  </core:cityObjectMember>
  <core:cityObjectMember>
    <bldg:Building gml:id="bldg-2">
      <gml:name>Building Two</gml:name>
      <bldg:lod2MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-roof-2">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-roof-2">
                  <gml:posList>20 0 8 26 0 8 26 6 8 20 6 8 20 0 8</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod2MultiSurface>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>
