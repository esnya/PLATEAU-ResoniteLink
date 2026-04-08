# Plateau.ResoniteLink

<img width="2560" height="1440" alt="2026-04-08 03 02 41" src="https://github.com/user-attachments/assets/7dac58c7-8855-4362-855d-f12e884dc05e" />

Plateau.ResoniteLink is a .NET 10 project for bringing [PLATEAU](https://www.mlit.go.jp/plateau/) datasets into Resonite through [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink). It streams CityGML-derived city objects directly into Resonite so imported content can begin appearing as it is processed.

Import behavior and terminology are guided by [PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/).

Release tags use the `vX.Y.Z` format. Build outputs derive `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` from those tags, emitting numeric assembly versions without the `v` prefix.


## Scope

- Stream PLATEAU CityGML datasets from local folders or the official CKAN-backed remote ZIP/7z flow into a running ResoniteLink listener.
- Preserve deterministic mesh/material ordering, carry `ParameterizedTexture` appearance data where present, and fall back to bundled default materials when source textures are missing.
- Build dataset and mesh-code branches incrementally so large imports can start appearing in Resonite before the full request finishes.

## Known Limitations

- The current public surface is the CLI live-send pipeline; no standalone offline exporter or in-Resonite authoring workflow is shipped yet.
- Remote import requires an explicit direct CityGML ZIP/7z archive URL and then reuses the same local importer against the downloaded archive-backed source.
- The live adapter currently relies on `ImportMesh(ImportMeshRawData)` for meshes and `ImportTexture(ImportTexture2DFile)` for textures because that path returns usable asset URLs in the current ResoniteLink runtime.

## Runtime And Prerequisites

- Target runtime: .NET SDK 10.
- Host assumption: a running ResoniteLink listener reachable by `--resonitelink-port` or `--resonitelink-url`.
- Live testing workflow: see [docs/live-testing.md](docs/live-testing.md).
- Default material and DEM terrain imagery sources and provenance: see [docs/default-materials.md](docs/default-materials.md), `THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt`, and `THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt`.

## Codex Cloud Setup

For Codex Cloud / CI-like ephemeral environments, run the repository setup script:

```bash
./scripts/setup-codex-cloud.sh
```

The script bootstraps .NET SDK 10 when `dotnet` is missing, then runs restore + whitespace format verification + tests using the same command shape as this repository's CI policy.

## Usage

Restore dependencies:

```bash
dotnet restore Plateau.ResoniteLink.sln
```

Import a local dataset root into a running ResoniteLink listener:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 53394525 \
  --packages dem,bldg,brid,frn,tran,rwy,trk,tun,ubld,unf,veg \
  --source local \
  --local-source-path /path/to/plateau \
  --dem-terrain-mode heightmap \
  --dem-heightmap-meters-per-vertex 2.0 \
  --dem-heightmap-max-resolution 1024 \
  --resonitelink-port <port> \
  --resonitelink-connections 1 \
  --send-metrics
```

`--resonitelink-port` or `--resonitelink-url` is required. `--work-root` defaults to `runtime/<os>/resonite/` and is used only for generated live assets and the remote download cache. `--packages` accepts a comma-separated list of official PLATEAU `udx/<package>/` names; when omitted, the CLI defaults to `dem,bldg,brid,frn,tran,rwy,trk,tun,ubld,unf,veg`. `--resonitelink-connections` defaults to `1`. `--send-metrics` enables opt-in `System.Diagnostics.Metrics` instrumentation with low-cardinality counters, histograms, and a CLI summary. DEM output stays on the existing mesh path by default; `--dem-terrain-mode heightmap` switches `dem` to a `GridMesh` + height texture path, with `--dem-heightmap-meters-per-vertex` and `--dem-heightmap-max-resolution` controlling sampling density and the safety cap. Option names follow PLATEAU SDK for Unity where practical: `--local-source-path` matches `DatasetSourceConfigLocal.LocalSourcePath`, and `--server-url` matches `DatasetSourceConfigRemote.ServerUrl`.

Import an official PLATEAU CityGML ZIP/7z archive online from an explicit archive URL:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source remote \
  --resonitelink-port <port>
```

`--source remote` does not perform any built-in dataset search. Pass `--server-url` as a direct CityGML ZIP/7z archive URL, and the CLI downloads it into `runtime/<os>/resonite/cache/remote/<dataset>/<archive-hash>/` before running the same local importer against the cached archive. The cache key is based on the archive URL rather than the requested mesh code, so repeated imports can reuse the same archive even when the detailed mesh code changes.

To find the official identifier and archive URL:

1. Open the official PLATEAU dataset page on `www.geospatial.jp`.
2. Copy the dataset page slug from the URL path, for example `plateau-20202-matsumoto-shi-2020`. Use that value as the canonical dataset identifier for `--dataset`.
3. Open the `CityGML` resource on that page and copy the direct `.zip` or `.7z` download URL. Use that value for `--server-url`.

Built-in search is intentionally out of scope so the CLI contract stays deterministic and aligned with explicit user-provided identifiers.

To reuse already-downloaded data, switch back to local import and point `--local-source-path` at either a dataset directory, a cached ZIP/7z archive, or an ancestor directory under `runtime/<os>/resonite/cache/remote/<dataset>/`. The importer resolves the nearest descendant dataset root that contains `udx/`, and it can also open a cached archive file transparently.

Build live into Resonite through ResoniteLink from Windows:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source remote \
  --resonitelink-port <port>
```

The live path connects to `ws://localhost:<port>/`, opens as many ResoniteLink sessions as configured by `--resonitelink-connections` (default `1`, so one session by default), imports mesh and texture assets through official ResoniteLink import messages, creates dataset and mesh-code slots, attaches a dataset-level `License` component with PLATEAU attribution text, and then builds the required Resonite components for the imported scene. Shared slot and component IDs are initialized once and reused across workers, city objects already placed in the target session are skipped before mesh/material placement, and city-object sends are distributed across the configured connections so large mesh-code imports can overlap live output without requiring a full in-memory batch.

Re-running `build` against the same ResoniteLink session and dataset appends new branches under the existing dataset instead of creating a separate dataset root. Each city object is placed under the mesh-code branch that actually owns its source data, so parent-mesh content can stay under a shorter mesh code such as `533945` while request-specific content stays under `53394525`. Mesh-code roots are positioned by slot offsets so neighboring imports line up, and already-placed objects under an existing mesh-code branch are not re-sent.

The current live adapter uses `ImportMesh(ImportMeshRawData)` for meshes and `ImportTexture(ImportTexture2DFile)` for textures, because the current ResoniteLink runtime returns a usable mesh asset URL on the raw-data path.

Validate formatting, analyzers, and tests:

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release -m:1 -p:UseSharedCompilation=false
```

## License Notes

- The repository root is licensed under [MIT](LICENSE).
- Imported PLATEAU datasets are not re-licensed by this repository. Their use remains subject to the original PLATEAU terms in the [PLATEAU Site Policy](https://www.mlit.go.jp/plateau/site-policy/), which currently states that published PLATEAU content is generally available under PDL 1.0-compatible terms, requires source attribution, and requires marking edits/derivative use where applicable.
- PLATEAU SDK for Unity is a separate upstream MIT-licensed project; a local copy of that license is tracked in `THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt`.
- Bundled default material textures under `src/Plateau.ResoniteLink.Cli/Assets/DefaultMaterials/` are sourced from AmbientCG and tracked as CC0 1.0 in `THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt`.
- The default DEM terrain imagery overlay is not a bundled asset. It is generated from the Geospatial Information Authority of Japan (GSI) seamless photo tile endpoint `https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg`; provenance and usage notes are tracked in `THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt`.
- NuGet and other runtime dependencies keep their own upstream licenses. Before redistributing binaries or vendored assets, review the package metadata and upstream license terms for the exact versions you ship.

PLATEAU guidance:

- The [PLATEAU Start Guide](https://www.mlit.go.jp/plateau/start-guide/) states that PLATEAU 3D city model copyrights belong to the respective local governments and that the datasets are provided as open data under licenses such as PDL 1.0, CC BY 4.0, ODC BY, or ODbL depending on the source dataset.
- When publishing derived content or redistributed data, keep the original dataset-level attribution and check whether any dataset-specific restrictions or measurement-law constraints apply.
