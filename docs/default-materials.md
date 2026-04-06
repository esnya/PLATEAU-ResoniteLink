# Default Materials

Textureless PLATEAU city objects now fall back to bundled default textures that mirror the PLATEAU SDK for Unity approach of assigning default materials by feature type. Fallback families can expose one or more variants, and the importer picks one deterministically from the city-object/material key so repeated imports stay stable.

## Category Mapping

The current package buckets are derived from the full official package candidate set the importer supports in Unity-SDK-aligned naming:

- `building`: `bldg`, `ubld`
- `road`: `tran`, `rwy`, `squr`, `trk`
- `wireframe overlay`: `area`, `fld`, `htd`, `ifld`, `lsld`, `luse`, `rfld`, `tnm`, `urf`
- `vegetation`: `veg`
- `city furniture`: `frn`
- `other solid fallback`: `brid`, `cons`, `gen`, `tun`, `unf`, `wtr`, `wwy`
- `special case`: `dem` keeps its generated terrain overlay path and does not use the bundled fallback families

The package-to-material mapping is centralized in `PlateauPackageCatalog`, and tests assert that every supported non-`dem` package belongs to exactly one material bucket so Unity-SDK package coverage and fallback policy stay in sync.
`frn` now resolves through a dedicated `city-furniture` fallback family instead of sharing the generic `other` bucket.
As of `PLATEAU-SDK-for-Unity` [`v4.2.0`](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/releases/tag/v4.2.0), published on 2026-03-12, Unity maps `PredefinedCityModelPackage.CityFurniture` to `PlateauDefaultCityFurniture`, whose default texture set uses a generic metal look. ResoniteLink follows that look, but the checked-in fallback texture data is sourced directly from AmbientCG instead of copying the Unity SDK asset files.

## `frn` Sampling Note

Sampling the Unity SDK's `TestDataTokyoMini` `frn` fixture showed that detailed city furniture is largely texture-driven:

- `53394525_frn_6697_sjkms_op.gml` contains 2 `frn:CityFurniture` objects and uses `lod2Geometry`
- it contains 34 `ParameterizedTexture` elements that reference 17 unique texture images
- it contains no `X3DMaterial`
- 467 of 481 polygons have explicit texture targets, leaving a small untextured remainder across horizontal, sloped, and vertical faces

That sample supports the current policy:

- when dataset textures exist, keep UV-based dataset materials
- when some polygons in `frn` remain untextured, fall back with triplanar projection instead of a building-style facade/roof split

## Bundled Assets

The repository bundles the following fallback materials:

- `facade`:
  `Facade018A_2K-JPG_Color.jpg`, `Facade019A_2K-JPG_Color.jpg`, `Facade020A_2K-JPG_Color.jpg`
- `roof`:
  `Concrete012_2K-JPG_Color.jpg`, `Concrete033_2K-JPG_Color.jpg`
- `road`:
  `Asphalt020L_2K-JPG_Color.jpg`, `Asphalt023L_2K-JPG_Color.jpg`
- `city-furniture`:
  `Metal032_2K-JPG_Color.jpg`
- `other`:
  `Concrete012_2K-JPG_Color.jpg`, `Ground054_2K-JPG_Color.jpg`

Sources:

- `Facade018A`: <https://ambientcg.com/view?id=Facade018A>
- `Facade019A`: <https://ambientcg.com/view?id=Facade019A>
- `Facade020A`: <https://ambientcg.com/view?id=Facade020A>
- `Concrete012`: <https://ambientcg.com/view?id=Concrete012>
- `Concrete033`: <https://ambientcg.com/view?id=Concrete033>
- `Asphalt020L`: <https://ambientcg.com/view?id=Asphalt020L>
- `Asphalt023L`: <https://ambientcg.com/view?id=Asphalt023L>
- `Ground054`: <https://ambientcg.com/view?id=Ground054>
- `Metal032`: <https://ambientcg.com/view?id=Metal032>

All bundled fallback textures currently checked into this repository are sourced from AmbientCG and distributed under CC0 1.0.
The local license tracking note is stored in `THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt`.

Source tracking by checked-in asset family:

- `default-materials/facade/Facade018A_2K-JPG_*` -> AmbientCG `Facade018A` -> <https://ambientcg.com/view?id=Facade018A>
- `default-materials/facade/Facade019A_2K-JPG_*` -> AmbientCG `Facade019A` -> <https://ambientcg.com/view?id=Facade019A>
- `default-materials/facade/Facade020A_2K-JPG_*` -> AmbientCG `Facade020A` -> <https://ambientcg.com/view?id=Facade020A>
- `default-materials/roof/Concrete012_2K-JPG_*` -> AmbientCG `Concrete012` -> <https://ambientcg.com/view?id=Concrete012>
- `default-materials/roof/Concrete033_2K-JPG_*` -> AmbientCG `Concrete033` -> <https://ambientcg.com/view?id=Concrete033>
- `default-materials/road/Asphalt020L_2K-JPG_*` -> AmbientCG `Asphalt020L` -> <https://ambientcg.com/view?id=Asphalt020L>
- `default-materials/road/Asphalt023L_2K-JPG_*` -> AmbientCG `Asphalt023L` -> <https://ambientcg.com/view?id=Asphalt023L>
- `default-materials/other/Ground054_2K-JPG_*` -> AmbientCG `Ground054` -> <https://ambientcg.com/view?id=Ground054>
- `default-materials/other/Concrete012_2K-JPG_*` -> AmbientCG `Concrete012` -> <https://ambientcg.com/view?id=Concrete012>
- `default-materials/city-furniture/Metal032_2K-JPG_*` -> AmbientCG `Metal032` -> <https://ambientcg.com/view?id=Metal032>

The `city-furniture` metallic map is not a verbatim upstream file. `Metal032_2K-JPG_Metallic.png` is derived from the AmbientCG `Metal032_2K-JPG_Metalness.jpg` and `Metal032_2K-JPG_Roughness.jpg` maps and repacked for Resonite `PBS_Metallic`.

The checked-in files keep only the maps that the live builder consumes directly:

- `*_Color.jpg` for albedo
- `*_NormalGL.jpg` for normal mapping
- `*_Height.jpg` for parallax height
- `*_Metallic.png` for Resonite's packed metallic map when the source material exposes roughness data
- Facade fallback keeps the facade-specific `*_Emission.jpg` maps, but the selected `018A/019A/020A` sub-variants were re-picked by comparing emissive pixel coverage and taking the lowest-emission option in each family

Bundled metallic maps follow the Resonite wiki packing for `PBS_Metallic`: red stores metallic, green stores occlusion or height, and alpha stores smoothness. When a bundled `HeightMap` is assigned, the live builder also writes a reduced `HeightScale` of `0.002` so the parallax effect stays close to one tenth of the material's default strength.
