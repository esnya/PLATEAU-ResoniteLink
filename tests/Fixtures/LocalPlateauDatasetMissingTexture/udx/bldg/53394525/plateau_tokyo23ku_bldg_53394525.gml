<?xml version="1.0" encoding="UTF-8"?>
<core:CityModel
  xmlns:app="http://www.opengis.net/citygml/appearance/2.0"
  xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
  xmlns:core="http://www.opengis.net/citygml/2.0"
  xmlns:gml="http://www.opengis.net/gml">
  <app:appearanceMember>
    <app:Appearance>
      <app:surfaceDataMember>
        <app:ParameterizedTexture>
          <app:imageURI>appearance/missing-roof.png</app:imageURI>
          <app:target uri="#poly-roof-1">
            <app:TexCoordList>
              <app:textureCoordinates ring="#ring-roof-1">0 0 1 0 1 1 0 1 0 0</app:textureCoordinates>
            </app:TexCoordList>
          </app:target>
        </app:ParameterizedTexture>
      </app:surfaceDataMember>
      <app:surfaceDataMember>
        <app:X3DMaterial>
          <app:diffuseColor>0.52 0.62 0.72</app:diffuseColor>
          <app:target uri="#poly-roof-1" />
        </app:X3DMaterial>
      </app:surfaceDataMember>
    </app:Appearance>
  </app:appearanceMember>
  <core:cityObjectMember>
    <bldg:Building gml:id="bldg-missing-texture">
      <gml:name>Fallback Building</gml:name>
      <bldg:lod2MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id="poly-roof-1">
              <gml:exterior>
                <gml:LinearRing gml:id="ring-roof-1">
                  <gml:posList>0 0 5 4 0 5 4 4 5 0 4 5 0 0 5</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod2MultiSurface>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>
