# PlateauResoniteLink

<img width="2560" height="1440" alt="2026-04-08 03 02 41" src="https://github.com/user-attachments/assets/7dac58c7-8855-4362-855d-f12e884dc05e" />

PlateauResoniteLink is a .NET 10 CLI for streaming [PLATEAU](https://www.mlit.go.jp/plateau/) CityGML datasets into Resonite through [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink). Import behavior and terminology stay aligned with [PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/). GitHub Releases are the canonical changelog, and each `vX.Y.Z` release publishes a framework-dependent CLI asset named `PlateauResoniteLink-cli-vX.Y.Z.zip`.

This README is the canonical human-readable scope statement for the current `beta` branch. Keep shipped, pending, and intentionally regressed behavior aligned here and in tests instead of reviving a separate requirements document.

## Scope

Shipped:
- Stream local PLATEAU datasets or explicit remote CityGML ZIP/7z archives into a running ResoniteLink listener.
- Accept `--citygml-source` and `--geotiff-source` as path-or-URL inputs, with `http`/`https` treated as remote URLs and other values treated as local filesystem paths.
- Inspect local dataset directories or local ZIP/7z archives with built-in `search` and `stats` commands before import.
- Treat `--resonitelink-connections` as a shipped live-send option, with a default live-send pool size of 4.
- Preserve deterministic mesh/material ordering, keep `ParameterizedTexture` appearance data where present, and fall back to bundled default materials when source textures are missing.
- After source bootstrap completes, build dataset and mesh-code branches incrementally so imported content can begin appearing in Resonite before the full live send completes.
- Persist terrain imagery tiles under a local cache by default so repeated DEM imports can reuse already downloaded PLATEAU Ortho or fallback GSI tiles.

Pending:
- Target-agnostic IR extraction and the deeper `Targets.Resonite` versus `Transport.ResoniteLink` split are internal follow-up work, not completed guarantees of this release.

Intentionally regressed:
- A standalone requirements document is not maintained as a release-truth surface. Product scope lives in `README.md` and tests, while live-send execution guidance is kept in the Coding Agent skill under `.agents/skills/resonite-live-send-debug/`.

## Runtime And Prerequisites

- Target runtime: .NET SDK 10. Release assets also require .NET 10.
- A running ResoniteLink listener reachable by `--resonitelink-port` or `--resonitelink-url` is required.
- Live adapter asset import uses `ImportMesh(ImportMeshRawData)` for meshes and `ImportTexture` raw payloads for textures, including bundled common materials, dataset-derived textures, and generated textures.
- ResoniteLink entity IDs are treated as session-scoped opaque values. For successful create operations, the resolved `Response` ID is authoritative within the session; requested IDs are only batch-local hints for per-cityObject DataModel batches, must not be persisted or reused across sessions, and reuse discovery is handled separately from create confirmation.

## Quick Start

Before opening or updating a pull request, run the canonical repository verification command sequence:

```bash
dotnet restore PlateauResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build PlateauResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test PlateauResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

For contributor workflow details, environment bootstrap guidance, and verification ownership, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Usage

Local import example:

```bash
dotnet run --project src/PlateauResoniteLink.Cli -- \
  build \
  --dataset plateau-20202-matsumoto-shi-2020 \
  --mesh-code 54372778 \
  --citygml-source /path/to/plateau \
  --resonitelink-port <port>
```

Remote archive example:

```bash
dotnet run --project src/PlateauResoniteLink.Cli -- \
  build \
  --dataset plateau-20202-matsumoto-shi-2020 \
  --mesh-code 54372788 \
  --citygml-source https://example.invalid/plateau-20202-matsumoto-shi-2020_citygml.zip \
  --geotiff-source https://example.invalid/53394525.tif \
  --resonitelink-port <port>
```

`--resonitelink-port` or `--resonitelink-url` is required. `--citygml-source` accepts an extracted dataset directory, a local `.zip` / `.7z` archive, or a direct `.zip` / `.7z` CityGML archive URL. `--geotiff-source` accepts a local `.tif` / `.tiff` file, a local `.zip` / `.7z` archive, or a direct `.tif` / `.tiff` / `.zip` / `.7z` URL. Built-in dataset search is not performed for remote URLs.

Local inspection examples:

```bash
dotnet run --project src/PlateauResoniteLink.Cli -- \
  search \
  --citygml-source /path/to/plateau-or-archive.zip \
  --mesh-code 5437277.
```

```bash
dotnet run --project src/PlateauResoniteLink.Cli -- \
  stats \
  --citygml-source /path/to/plateau-or-archive.zip
```

`search` and `stats` inspect local dataset directories and local `.zip` / `.7z` archives. Remote import still requires an explicit direct archive URL.

By default, the CLI prints milestone-level progress and keeps detailed per-file and live-send trace logs hidden. Add `--verbose` when you need the debug-level import and ResoniteLink trace output.

When `--work-root` is omitted, the CLI stores dataset-local archives and live temporary files under `local/<dataset>/`. Terrain tile downloads are cached separately under the local app-data cache root by default; override that path with `--terrain-tile-cache-root` or disable cross-run tile caching with `--disable-terrain-tile-cache`.

## Further Reading

- Contributor workflow: [CONTRIBUTING.md](CONTRIBUTING.md)
- Coding-agent live workflow: [.agents/skills/resonite-live-send-debug/SKILL.md](.agents/skills/resonite-live-send-debug/SKILL.md)

## License And Provenance

- The repository source code is licensed under [MIT](LICENSE).
- Imported PLATEAU datasets are not re-licensed by this repository. Review each dataset's README, metadata, download page, and rights notices before importing, redistributing, or publishing derived content.
- The [PLATEAU Site Policy](https://www.mlit.go.jp/plateau/site-policy/) is only the portal-level default and does not override dataset-specific terms.
- The [PLATEAU Start Guide](https://www.mlit.go.jp/plateau/start-guide/) notes that dataset terms vary by source, including licenses such as PDL 1.0, CC BY 4.0, ODC BY, or ODbL.
- PLATEAU SDK for Unity is a separate upstream MIT-licensed project. A local copy of that license is tracked in `THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt`.
- Bundled default material textures under `src/PlateauResoniteLink/Assets/DefaultMaterials/` are sourced from AmbientCG and tracked in `THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt`.
- The default DEM terrain imagery overlay is not bundled. It is generated from the PLATEAU ortho tile endpoint `https://api.plateauview.mlit.go.jp/tiles/plateau-ortho-2023/{z}/{x}/{y}.png`, with GSI seamless photo fallback `https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg`. Repository-local notes for the fallback source live in `THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt`.
- NuGet packages and other runtime dependencies keep their own upstream licenses. Review the exact versions you ship before redistributing binaries or vendored assets.
