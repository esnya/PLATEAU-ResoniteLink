---
name: plateau-dataset-estimate
description: Estimate PLATEAU dataset quality, import cost, rendering risk, texture VRAM, geometry VRAM, DEM terrain imagery sources, terrain mesh mode tradeoffs, and Grid resolution choices for PLATEAU-ResoniteLink. Use when the user asks for rough or evidence-backed pre-import feasibility sizing of a PLATEAU CityGML dataset from official metadata, bounded archive sampling, package/LOD coverage, texture/geometry sizing, DEM surface source sampling, or Resonite rendering performance risk.
---

# PLATEAU Dataset Estimate

Use this skill to produce a bounded dataset-level estimate, not a benchmark result. Separate observed facts, calculations, assumptions, and unverified risks. Prefer official metadata and small archive samples before downloading full datasets or running live imports.

## Workflow

1. Identify the dataset from official or primary metadata.
   - Prefer G空間情報センター CKAN `package_show`, PLATEAU portal data, and official asset URLs.
   - Confirm dataset id, fiscal year, metadata update time, license, specification version, and resource URLs.
   - If latest data is requested, verify current metadata online instead of relying on memory.

2. Inspect package weight without full extraction.
   - For remote ZIP files, use HTTP `HEAD` and ZIP central-directory range reads when possible.
   - Record total compressed size, entry count, package-level `.gml` counts, texture counts, compressed and uncompressed bytes.
   - For local files, `7z l`, `tar`, or a small ZIP central-directory reader is acceptable.

3. Estimate texture VRAM from dimensions, not JPEG bytes alone.
   - Sample representative images: largest files plus evenly distributed files.
   - Read actual image dimensions after ZIP decompression if the archive deflates JPEG entries.
   - Estimate total texels from sampled `jpeg_bytes_per_pixel`, then compute:
     - BC1: `texels * 0.5`
     - BC3/BC7: `texels * 1.0`
     - RGBA32: `texels * 4.0`
     - mip chain: multiply by `4/3`
   - Report texture estimates as a range because sampled JPEG compression ratios are content-biased.

4. Estimate DEM surface imagery separately from DEM terrain geometry.
   - Check current defaults in code, not memory:
     - `DemTerrainTextureDefaults.PlateauOrthoUrlTemplate`
     - `PlateauOrthoZoomLevel`
     - `FallbackZoomLevel`
     - `GsiFallbackUrlTemplate`
     - `MaxTextureSize`
   - Resolve or approximate DEM overlay regions from DEM `gml:Envelope` bounds.
   - Sample tile availability at center and corners for each region:
     - first PLATEAU-Ortho primary zoom
     - then PLATEAU-Ortho fallback zoom
     - then GSI fallback zoom
   - Use the first source that produces renderable coverage, and note mixed fallback coverage.
   - Apply `TerrainTextureAssetGenerator` behavior: crop tile mosaic, make opaque, resize to `MaxTextureSize` if needed, then round up to a power-of-two canvas.

5. Estimate geometry separately.
   - Prefer actual CLI/import metrics or generated mesh stats when available.
   - If triangle counts are unavailable, classify the geometry estimate as coarse.
   - For DEM `--terrain-mesh grid`, estimate Grid Points from bounds and the proposed grid settings:
     - raw columns/rows: `ceil(width_m / meters_per_vertex) + 1`, `ceil(height_m / meters_per_vertex) + 1`
     - clamped columns/rows: each axis is capped by `--terrain-grid-max-resolution`
     - points: `columns * rows`
     - quads: `(columns - 1) * (rows - 1)`
     - vertex payload coarse range: `32-64 bytes * points`
     - indices coarse range: `6 * 4 bytes * quads`
   - Include at least default, preview, and quality-oriented Grid settings when the user asks for Grid resolution guidance.
   - Do not count Grid displacement as ordinary BC texture VRAM unless there is evidence that the engine keeps it as a GPU texture. Treat it as CPU-side or implementation-dependent unless live inspection proves otherwise.
   - For DEM `--terrain-mesh static`, flag higher uncertainty: CityGML DEM geometry can preserve more source detail but may import and render heavier than a bounded Grid.

6. Compare terrain mesh modes and package scopes.
   - `--terrain-mesh grid` is usually preferable for full-area DEM when bounded by `--terrain-grid-meters-per-vertex` and `--terrain-grid-max-resolution`.
   - `--terrain-mesh static` can preserve local source detail but has higher parse/import/rendering variance.
   - Package selection and LOD exclusion directly change scan, parse, texture, material, and geometry load.
   - For performance conclusions, prefer `import --verbose --send-metrics` and Resonite-side frame/VRAM observation over static estimates.

## Output Contract

Always include a combined resource table with texture, geometry, and total estimates:

```text
| Target | Texture VRAM | Geometry VRAM | Total |
|---|---:|---:|---:|
| bldg | ... | ... | ... |
| dem terrain grid | ... | ... | ... |
| other packages | ... | ... | ... |
| bldg + dem | ... | ... | ... |
```

When Grid is in scope, also include a Grid resolution table:

```text
| Grid setting | Columns x Rows | Grid Points | Geometry VRAM | Use |
|---|---:|---:|---:|---|
| default: 2.0 m, max 1024 | ... | ... | ... | baseline |
| preview | ... | ... | ... | faster import/render |
| quality | ... | ... | ... | higher DEM detail |
```

Then list:

- dataset id and source URLs
- source provenance and metadata freshness
- quality indicators from official metadata and package/LOD coverage
- observed archive facts
- package/file-size observations
- texture format assumptions
- texture VRAM estimate
- geometry VRAM estimate
- DEM surface source actually sampled or assumed
- DEM surface imagery cost
- terrain mesh mode and Grid resolution recommendation
- import and rendering performance risk
- uncertainty and next measurements

## Guardrails

- Do not present hypotheses as conclusions.
- Do not narrow the answer to VRAM when dataset quality, package/LOD coverage, import cost, or rendering risk can also be estimated from the same bounded investigation.
- Do not use raw RGBA-only accounting as the primary answer when compressed runtime formats are part of the question.
- Do not hide DEM surface imagery inside DEM geometry; report it as its own texture cost.
- Do not treat Grid as texture-only. In this repo it becomes GridMesh-style geometry plus implementation-dependent displacement data.
- Do not confuse DEM surface imagery `MaxTextureSize` with Grid geometry resolution; Grid Points are controlled by `--terrain-grid-meters-per-vertex` and `--terrain-grid-max-resolution`.
- Do not run a full live import just for a rough estimate unless the user asks for real measurement.
