# Reference: PLATEAU-SDK-for-UNITY

This project treats `PLATEAU-SDK-for-UNITY` as the reference implementation for import behavior.

## Align Now

- Make dataset selection explicit.
- Keep the spatial selection surface explicit and represent it as PLATEAU `mesh-code`.
- Follow official PLATEAU package naming under `udx/<package>/` so Unity-side package coverage carries over without importer-specific aliases.
- Match Unity-side `dem` terrain texturing by stitching aerial or map tiles into a shared Web Mercator overlay and assigning DEM UVs against that geographic overlay instead of a repeating fallback texture.
- Bring low-LOD `tran` terrain fitting closer to Unity road adjustment by subdividing long transportation quads before DEM conformance.
- Preserve room for both `DatasetSourceConfigLocal` and `DatasetSourceConfigRemote` style inputs in the import model, including Unity-aligned names such as `LocalSourcePath` and `ServerUrl`.
- Stabilize the import contract and processing boundaries before building a GUI.
- Prefer large-data handling that can stream tiles or city objects incrementally instead of assuming one full in-memory batch.

## Align Later

- Map Unity-side package and mesh-code concepts into the Resonite import flow without reintroducing ad-hoc CLI terms.
- Normalize geospatial origin, elevation, and mesh-code anchoring into Resonite space.
- Extend the CLI contract for textures, attributes, and LOD selection.

## References

- GitHub repository: <https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity>
- Documentation portal: <https://project-plateau.github.io/PLATEAU-SDK-for-Unity/>
