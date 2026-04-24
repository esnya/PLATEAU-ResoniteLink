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
   - When a local dataset directory or archive exists, run the repo stats command first and treat it as the primary local-source inventory:
     - `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- stats --citygml-source <path> [--packages <csv>] --format json`
   - Use `stats` output for package counts, mesh-code counts, LOD coverage, and archive-derived VRAM fields before falling back to ad hoc archive scans.
   - For remote ZIP files, use HTTP `HEAD` and ZIP central-directory range reads when possible.
   - Record total compressed size, entry count, package-level `.gml` counts, texture counts, compressed and uncompressed bytes.
   - For local files, use `7z l`, `tar`, or a small ZIP central-directory reader only for facts that `stats` does not expose.

3. Estimate renderer texture VRAM from dimensions, not JPEG bytes alone.
   - If `stats --format json` includes `archiveVramEstimate.rendererTextureVram`, use those values for archive-referenced textures and label them as source-observed estimates.
   - Sample representative images: largest files plus evenly distributed files.
   - Read actual image dimensions after ZIP decompression if the archive deflates JPEG entries.
   - Check effective alpha from archive pixels, not only image mode:
     - Treat JPEG as no-alpha.
     - For PNG and other alpha-capable images, inspect the alpha channel and classify images whose pixels are all alpha 255 as no-effective-alpha.
     - Count only images with non-opaque pixels as effective-alpha images.
   - Estimate total texels from sampled `jpeg_bytes_per_pixel`, then compute renderer GPU texture estimates:
     - BC1: `texels * 0.5`
     - BC3: `texels * 1.0`
     - mip chain: multiply by `4/3`
   - For post-send Resonite renderer GPU estimates, assume PLATEAU imagery without effective alpha is BC1 after import. Use BC3 only for images whose archive pixels contain non-opaque alpha. Do not use BC7 as the default PLATEAU estimate unless live engine state proves it.
   - Report RGBA32 separately only as sender payload, CPU-side, or upper-bound comparison. Do not use raw RGBA32 as the primary renderer VRAM estimate when compressed runtime formats are in scope.
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

5. Estimate renderer geometry separately.
   - Prefer actual CLI/import metrics, `stats --format json` `archiveVramEstimate.rendererGeometryVram`, or generated mesh stats when available.
   - If triangle counts are unavailable, classify the geometry estimate as coarse.
   - Treat `stats` geometry as an archive-derived GML `posList` estimate. It is useful for pre-import sizing, but it is not a post-tessellation/live Renderer measurement.
   - For DEM `--terrain-mesh static`, treat the source TIN as ordinary triangle mesh data that reaches the Unity renderer as mesh buffers. Flag higher uncertainty: CityGML DEM geometry can preserve more source detail but may import and render heavier than a bounded Grid.
   - For DEM `--terrain-mesh grid`, estimate Grid Points from bounds and the proposed grid settings:
     - raw columns/rows: `ceil(width_m / meters_per_vertex) + 1`, `ceil(height_m / meters_per_vertex) + 1`
     - clamped columns/rows: each axis is capped by `--terrain-grid-max-resolution`
     - points: `columns * rows`
     - quads: `(columns - 1) * (rows - 1)`
     - renderer vertex payload coarse range: `32-64 bytes * points`
     - renderer indices coarse range: `6 * 4 bytes * quads`
   - For DEM Grid, assume FrooxEngine reads the displacement texture on CPU and sends the generated GridMesh-style geometry to the Unity renderer. Count the generated GridMesh geometry in renderer VRAM.
   - Do not count Grid displacement as ordinary renderer texture VRAM. Report it separately as CPU/engine-side or payload cost, typically `16 bytes * points` for the current float4 height data, unless live engine state proves it is retained as a GPU-sampled texture.
   - Include at least default, preview, and quality-oriented Grid settings when the user asks for Grid resolution guidance.

6. Compare terrain mesh modes and package scopes.
   - `--terrain-mesh grid` is usually preferable for full-area DEM when bounded by `--terrain-grid-meters-per-vertex` and `--terrain-grid-max-resolution`.
   - `--terrain-mesh static` can preserve local source detail but has higher parse/import/rendering variance.
   - Package selection and LOD exclusion directly change scan, parse, texture, material, and geometry load.
   - For performance conclusions, prefer `import --verbose --send-metrics` and Resonite-side frame/VRAM observation over static estimates.

## Output Contract

Always include a combined resource table that labels renderer GPU memory separately from CPU/engine-side or sender payload costs:

```text
| Target | Renderer Texture VRAM | Renderer Geometry VRAM | CPU/engine-side extra | Renderer Total |
|---|---:|---:|---:|---:|
| bldg | ... | ... | ... | ... |
| dem terrain grid | ... | ... | ... | ... |
| other packages | ... | ... | ... | ... |
| bldg + dem | ... | ... | ... | ... |
```

When Grid is in scope, also include a Grid resolution table:

```text
| Grid setting | Columns x Rows | Grid Points | Renderer Geometry VRAM | Displacement/CPU-side cost | Use |
|---|---:|---:|---:|---:|---|
| default: 2.0 m, max 1024 | ... | ... | ... | ... | baseline |
| preview | ... | ... | ... | ... | faster import/render |
| quality | ... | ... | ... | ... | higher DEM detail |
```

Then list:

- dataset id and source URLs
- source provenance and metadata freshness
- quality indicators from official metadata and package/LOD coverage
- observed archive facts
- stats command used, version/command line, or reason it was unavailable
- package/file-size observations
- texture format assumptions
- renderer texture VRAM estimate
- renderer geometry VRAM estimate
- CPU/engine-side or payload memory estimate when relevant
- DEM surface source actually sampled or assumed
- DEM surface imagery cost
- terrain mesh mode and Grid resolution recommendation
- import and rendering performance risk
- uncertainty and next measurements

## Guardrails

- Do not present hypotheses as conclusions.
- Do not manually rescan a local archive for package/LOD/VRAM facts before trying `stats` when the local source is available.
- Do not narrow the answer to VRAM when dataset quality, package/LOD coverage, import cost, or rendering risk can also be estimated from the same bounded investigation.
- Do not use raw RGBA-only accounting as the primary answer when compressed runtime formats are part of the question.
- Do not use BC7 as the default PLATEAU renderer texture estimate without live engine-state evidence.
- Do not hide DEM surface imagery inside DEM geometry; report it as its own texture cost.
- Do not treat Grid as texture-only. In this repo it becomes GridMesh-style geometry plus CPU/engine-side displacement data.
- Do not count Grid displacement as renderer texture VRAM by default. If the user asks for renderer VRAM, keep displacement separate from Renderer Total unless live inspection proves the engine keeps it as a GPU texture.
- Do not confuse DEM surface imagery `MaxTextureSize` with Grid geometry resolution; Grid Points are controlled by `--terrain-grid-meters-per-vertex` and `--terrain-grid-max-resolution`.
- Do not run a full live import just for a rough estimate unless the user asks for real measurement.
