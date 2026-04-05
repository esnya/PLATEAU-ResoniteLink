# Default Materials

Textureless PLATEAU city objects now fall back to bundled default textures that mirror the PLATEAU SDK for Unity approach of assigning default materials by feature type. Each fallback family now has multiple variants, and the importer picks one deterministically from the city-object/material key so repeated imports stay stable.

## Category Mapping

- `building`: `bldg`, `ubld`
- `road`: `tran`, `rwy`, `squr`, `trk`
- `other`: every remaining package

The current ResoniteLink path splits the fallback by material intent:

- detailed dataset textures keep UV-based `PBS_Metallic`
- untextured building facades use UV-based facade textures
- untextured roofs, roads, and other packages use `PBS_TriplanarMetallic`
- non-installation overlays such as `area`, `luse`, `fld`, `ifld`, `rfld`, `lsld`, `tnm`, `htd`, and `urf` render as `WireframeMaterial`

For buildings, facade detection now prefers CityGML thematic surfaces such as `bldg:WallSurface` and falls back to polygon orientation only when that semantic wrapper is absent. Near-vertical surfaces still default to facade UVs in the fallback path; roof- and ground-like semantics keep triplanar projection.

Generated facade UVs now use a fixed repeat density instead of normalizing every polygon to `0..1`, so bundled facade textures tile across larger wall spans.
The default facade fallback now uses facade-specific assets only. Brick-like materials are no longer selected for wall fallback. The material carries the physical repeat scale, while generated facade UVs stay wall-local and snap vertically so the bottom and top edges land on repeat boundaries.

## Bundled Assets

The repository bundles the following 2K AmbientCG materials under CC0:

- `facade`:
  `Facade018C_2K-JPG_Color.jpg`, `Facade019A_2K-JPG_Color.jpg`, `Facade020A_2K-JPG_Color.jpg`
- `roof`:
  `Concrete012_2K-JPG_Color.jpg`, `Concrete033_2K-JPG_Color.jpg`
- `road`:
  `Asphalt020L_2K-JPG_Color.jpg`, `Asphalt023L_2K-JPG_Color.jpg`
- `other`:
  `Concrete012_2K-JPG_Color.jpg`, `Ground054_2K-JPG_Color.jpg`

Sources:

- `Facade018C`: <https://ambientcg.com/view?id=Facade018C>
- `Facade019A`: <https://ambientcg.com/view?id=Facade019A>
- `Facade020A`: <https://ambientcg.com/view?id=Facade020A>
- `Concrete012`: <https://ambientcg.com/view?id=Concrete012>
- `Concrete033`: <https://ambientcg.com/view?id=Concrete033>
- `Asphalt020L`: <https://ambientcg.com/view?id=Asphalt020L>
- `Asphalt023L`: <https://ambientcg.com/view?id=Asphalt023L>
- `Ground054`: <https://ambientcg.com/view?id=Ground054>

The checked-in files keep only the maps that the live builder consumes directly:

- `*_Color.jpg` for albedo
- `*_NormalGL.jpg` for normal mapping
- `*_Height.jpg` for parallax height
- `*_Metallic.png` for Resonite's packed metallic map when the source material exposes roughness data
- `*_Emission.jpg` when the source material includes an emissive texture

Bundled metallic maps follow the Resonite wiki packing for `PBS_Metallic`: red stores metallic, green stores occlusion or height, and alpha stores smoothness. When a bundled `HeightMap` is assigned, the live builder also writes a reduced `HeightScale` of `0.002` so the parallax effect stays close to one tenth of the material's default strength.
