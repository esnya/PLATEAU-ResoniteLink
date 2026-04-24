---
name: plateau-dataset-estimate
description: Estimate PLATEAU dataset quality, import cost, rendering risk, texture VRAM, geometry VRAM, DEM terrain imagery sources, terrain mesh mode tradeoffs, and Grid resolution choices for PLATEAU-ResoniteLink. Use when the user asks for rough or evidence-backed pre-import feasibility sizing of a PLATEAU CityGML dataset from official metadata, bounded archive sampling, package/LOD coverage, texture/geometry sizing, DEM surface source sampling, or Resonite rendering performance risk.
---

# PLATEAU Dataset Estimate

この skill は benchmark 結果ではなく、境界を切った dataset-level estimate を作るために使います。観測事実、計算、前提、未確認リスクを分けて書いてください。full dataset download や live import の前に、公式 metadata と小さな archive sample を優先します。

## Workflow

1. 公式または一次 metadata から dataset を特定します。
   - G空間情報センター CKAN `package_show`、PLATEAU portal data、公式 asset URL を優先します。
   - dataset id、年度、metadata 更新時刻、license、specification version、resource URL を確認します。
   - 最新 data が求められている場合は、memory ではなく現在の online metadata を確認します。

2. full extraction なしで package weight を調べます。
   - remote ZIP は可能なら HTTP `HEAD` と ZIP central-directory range read を使います。
   - total compressed size、entry count、package ごとの `.gml` count、texture count、compressed/uncompressed bytes を記録します。
   - local file では `7z l`、`tar`、または小さな ZIP central-directory reader を使って構いません。

3. renderer texture VRAM は JPEG bytes だけではなく寸法から推定します。
   - largest files と evenly distributed files を代表 sample にします。
   - archive が JPEG entry を deflate している場合は、ZIP decompression 後に実 image dimensions を読みます。
   - Archive pixel から effective alpha を確認します。image mode だけでは判断しません。
     - JPEG は no-alpha として扱います。
     - PNG など alpha を持ち得る image は alpha channel を検査し、全 pixel が alpha 255 なら no-effective-alpha に分類します。
     - non-opaque pixel を含む image だけ effective-alpha image として数えます。
   - sampled `jpeg_bytes_per_pixel` から total texels を推定し、renderer GPU texture estimate として次で計算します。
     - BC1: `texels * 0.5`
     - BC3: `texels * 1.0`
     - mip chain: `4/3` を掛ける
   - 送信後の Resonite renderer GPU estimate では、有効な alpha を持たない PLATEAU imagery は import 後 BC1 と仮定します。Archive pixels に non-opaque alpha がある image だけ BC3 を使います。live engine state で証明されない限り、BC7 を PLATEAU の default estimate にしません。
   - RGBA32 は sender payload、CPU-side、または upper-bound comparison としてだけ別枠で報告します。compressed runtime format が scope に入っている場合、raw RGBA32 を renderer VRAM の主推定にしません。
   - JPEG compression ratio は content に偏るので、texture estimate は range で出します。

4. DEM surface imagery は DEM terrain geometry と分けて推定します。
   - 現在の code から以下を確認します。memory だけで決めないでください。
     - `DemTerrainTextureDefaults.PlateauOrthoUrlTemplate`
     - `PlateauOrthoZoomLevel`
     - `FallbackZoomLevel`
     - `GsiFallbackUrlTemplate`
     - `MaxTextureSize`
   - DEM `gml:Envelope` bounds から DEM overlay regions を解決または近似します。
   - 各 region の center/corners で tile availability を sample します。
     - PLATEAU-Ortho primary zoom
     - PLATEAU-Ortho fallback zoom
     - GSI fallback zoom
   - renderable coverage が取れる最初の source を使い、mixed fallback coverage は明記します。
   - `TerrainTextureAssetGenerator` の挙動を反映します。tile mosaic を crop し、opaque 化し、必要なら `MaxTextureSize` に resize して、power-of-two canvas に載せます。

5. renderer geometry は別枠で推定します。
   - 可能なら実 CLI/import metrics または generated mesh stats を優先します。
   - triangle count がない場合、geometry estimate は coarse と明記します。
   - DEM `--terrain-mesh static` では、source TIN を Unity renderer に mesh buffer として届く通常 triangle mesh data として扱います。uncertainty は高く明記します。source detail は保てますが、bounded Grid より import/rendering が重く振れることがあります。
   - DEM `--terrain-mesh grid` では、bounds と提案する grid settings から Grid Points を推定します。
     - raw columns/rows: `ceil(width_m / meters_per_vertex) + 1`, `ceil(height_m / meters_per_vertex) + 1`
     - clamped columns/rows: 各軸を `--terrain-grid-max-resolution` 以下に制限
     - points: `columns * rows`
     - quads: `(columns - 1) * (rows - 1)`
     - renderer vertex payload coarse range: `32-64 bytes * points`
     - renderer indices coarse range: `6 * 4 bytes * quads`
   - DEM Grid では、FrooxEngine が displacement texture を CPU で読み、生成後の GridMesh-style geometry を Unity renderer に送る前提で見積もります。生成された GridMesh geometry を renderer VRAM に計上します。
   - Grid displacement は通常の renderer texture VRAM として計上しません。live engine state で GPU sampled texture として保持される証拠がない限り、現在の float4 height data では典型的に `16 bytes * points` の CPU/engine-side または payload cost として別枠にします。
   - user が Grid resolution guidance を求めている場合は、default、preview、quality-oriented Grid settings を少なくとも含めます。

6. terrain mesh mode と package scope を比較します。
   - full-area DEM では `--terrain-grid-meters-per-vertex` と `--terrain-grid-max-resolution` で上限を切れる `--terrain-mesh grid` が基本的に有利です。
   - `--terrain-mesh static` は局所 source detail を保てますが、parse/import/rendering variance が大きくなります。
   - package selection と LOD exclusion は scan、parse、texture、material、geometry load に直接効きます。
   - performance conclusion には、静的見積もりより `import --verbose --send-metrics` と Resonite 側 frame/VRAM observation を優先します。

## Output Contract

renderer GPU memory と CPU/engine-side または sender payload cost を分けた combined resource table を必ず含めます。

```text
| Target | Renderer Texture VRAM | Renderer Geometry VRAM | CPU/engine-side extra | Renderer Total |
|---|---:|---:|---:|---:|
| bldg | ... | ... | ... | ... |
| dem terrain grid | ... | ... | ... | ... |
| other packages | ... | ... | ... | ... |
| bldg + dem | ... | ... | ... | ... |
```

Grid が scope に入る場合は、Grid resolution table も含めます。

```text
| Grid setting | Columns x Rows | Grid Points | Renderer Geometry VRAM | Displacement/CPU-side cost | Use |
|---|---:|---:|---:|---:|---|
| default: 2.0 m, max 1024 | ... | ... | ... | ... | baseline |
| preview | ... | ... | ... | ... | faster import/render |
| quality | ... | ... | ... | ... | higher DEM detail |
```

その後に以下を列挙します。

- dataset id と source URLs
- source provenance と metadata freshness
- official metadata と package/LOD coverage から得られる quality indicators
- observed archive facts
- package/file-size observations
- texture format assumptions
- renderer texture VRAM estimate
- renderer geometry VRAM estimate
- 関連する場合は CPU/engine-side または payload memory estimate
- 実際に sample した、または仮定した DEM surface source
- DEM surface imagery cost
- terrain mesh mode と Grid resolution recommendation
- import and rendering performance risk
- uncertainty と next measurements

## Guardrails

- hypothesis を conclusion として扱わないでください。
- 同じ bounded investigation から dataset quality、package/LOD coverage、import cost、rendering risk も見積もれる場合、回答を VRAM だけに狭めないでください。
- compressed runtime format が論点に入っている場合、raw RGBA だけの計算を主回答にしないでください。
- live engine-state evidence がない限り、BC7 を PLATEAU renderer texture estimate の default にしないでください。
- DEM surface imagery を DEM geometry に隠さず、texture cost として別に報告します。
- Grid を texture-only として扱わないでください。この repo では GridMesh-style geometry と CPU/engine-side displacement data になります。
- Grid displacement は default では renderer texture VRAM に計上しないでください。user が renderer VRAM を求めている場合、engine が GPU texture として保持していることを live inspection で確認できない限り、displacement は Renderer Total から分けます。
- DEM surface imagery の `MaxTextureSize` と Grid geometry resolution を混同しないでください。Grid Points は `--terrain-grid-meters-per-vertex` と `--terrain-grid-max-resolution` で決まります。
- user が実測を求めていない限り、rough estimate のためだけに full live import を実行しないでください。
