# Default Materials

Textureless PLATEAU city objects now fall back to bundled default textures that mirror the PLATEAU SDK for Unity approach of assigning default materials by feature type. Each fallback family now has multiple variants, and the importer picks one deterministically from the city-object/material key so repeated imports stay stable.

## Category Mapping

- `building`: `bldg`, `ubld`
- `road`: `tran`, `rwy`, `squr`, `trk`
- `other`: every remaining package

The current ResoniteLink path splits the fallback by projection mode:

- detailed dataset textures keep UV-based `PBS_Metallic`
- untextured building facades use UV-based facade textures
- untextured roofs, roads, and other packages use `PBS_TriplanarMetallic`

For buildings, facade detection is currently inferred from polygon orientation. Near-vertical surfaces are treated as facades; everything else falls back to triplanar.

## Bundled Assets

The repository bundles the following 2K albedo textures from AmbientCG under CC0:

- `facade`:
  `PaintedPlaster012_2K-JPG_Color.jpg`, `Facade019A_2K-JPG_Color.jpg`, `Bricks074_2K-JPG_Color.jpg`
- `roof`:
  `Concrete012_2K-JPG_Color.jpg`, `Concrete033_2K-JPG_Color.jpg`
- `road`:
  `Asphalt002_2K-JPG_Color.jpg`, `Road006_2K-JPG_Color.jpg`
- `other`:
  `Concrete012_2K-JPG_Color.jpg`, `Ground054_2K-JPG_Color.jpg`

Sources:

- `PaintedPlaster012`: <https://ambientcg.com/view?id=PaintedPlaster012>
- `Facade019A`: <https://ambientcg.com/view?id=Facade019A>
- `Bricks074`: <https://ambientcg.com/view?id=Bricks074>
- `Concrete012`: <https://ambientcg.com/view?id=Concrete012>
- `Concrete033`: <https://ambientcg.com/view?id=Concrete033>
- `Asphalt002`: <https://ambientcg.com/view?id=Asphalt002>
- `Road006`: <https://ambientcg.com/view?id=Road006>
- `Ground054`: <https://ambientcg.com/view?id=Ground054>

Only the albedo maps are embedded today. The live material builder wires them into either `PBS_Metallic` or `PBS_TriplanarMetallic`, depending on the fallback path above.
