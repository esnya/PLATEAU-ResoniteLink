# Plateau.ResoniteLink

<img width="2560" height="1440" alt="2026-04-08 03 02 41" src="https://github.com/user-attachments/assets/7dac58c7-8855-4362-855d-f12e884dc05e" />

Plateau.ResoniteLink is a .NET 10 CLI for streaming [PLATEAU](https://www.mlit.go.jp/plateau/) CityGML datasets into Resonite through [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink). Import behavior and terminology stay aligned with [PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/). GitHub Releases are the canonical changelog, and each `vX.Y.Z` release publishes a framework-dependent CLI asset named `Plateau.ResoniteLink-cli-vX.Y.Z.zip`.

This README is the canonical human-readable scope statement for the current `beta` branch. Keep shipped, pending, and intentionally regressed behavior aligned here and in tests instead of reviving a separate requirements document.

## Scope

Shipped:
- Stream local PLATEAU datasets or explicit remote CityGML ZIP/7z archives into a running ResoniteLink listener.
- Treat `--resonitelink-connections` as a shipped live-send option.
- Preserve deterministic mesh/material ordering, keep `ParameterizedTexture` appearance data where present, and fall back to bundled default materials when source textures are missing.
- After source bootstrap completes, build dataset and mesh-code branches incrementally so imported content can begin appearing in Resonite before the full live send completes.
- Keep LOD1 mesh bake and LOD2 atlas bake keyed by CityGML scope, package, LOD, and bake policy so emitted bake payloads do not depend on cityObject arrival order.

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
dotnet restore Plateau.ResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

For contributor workflow details, environment bootstrap guidance, and verification ownership, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Usage

Local import example:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset plateau-20202-matsumoto-shi-2020 \
  --mesh-code 54372778 \
  --source local \
  --local-source-path /path/to/plateau \
  --resonitelink-port <port>
```

Remote archive example:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset plateau-20202-matsumoto-shi-2020 \
  --mesh-code 54372788 \
  --source remote \
  --server-url https://example.invalid/plateau-20202-matsumoto-shi-2020_citygml.zip \
  --resonitelink-port <port>
```

`--resonitelink-port` or `--resonitelink-url` is required. `--source remote` requires a direct `.zip` or `.7z` CityGML archive URL and does not perform built-in dataset search.

By default, the CLI prints milestone-level progress and keeps detailed per-file and live-send trace logs hidden. Add `--verbose` when you need the debug-level import and ResoniteLink trace output.

When `--work-root` is omitted, the CLI stores dataset-local archives and live temporary files under `local/<dataset>/`.

## Further Reading

- Contributor workflow: [CONTRIBUTING.md](CONTRIBUTING.md)
- Coding-agent live workflow: [.agents/skills/resonite-live-send-debug/SKILL.md](.agents/skills/resonite-live-send-debug/SKILL.md)

## License And Provenance

- The repository source code is licensed under [MIT](LICENSE).
- Imported PLATEAU datasets are not re-licensed by this repository. Review each dataset's README, metadata, download page, and rights notices before importing, redistributing, or publishing derived content.
- The [PLATEAU Site Policy](https://www.mlit.go.jp/plateau/site-policy/) is only the portal-level default and does not override dataset-specific terms.
- The [PLATEAU Start Guide](https://www.mlit.go.jp/plateau/start-guide/) notes that dataset terms vary by source, including licenses such as PDL 1.0, CC BY 4.0, ODC BY, or ODbL.
- PLATEAU SDK for Unity is a separate upstream MIT-licensed project. A local copy of that license is tracked in `THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt`.
- Bundled default material textures under `src/Plateau.ResoniteLink/Assets/DefaultMaterials/` are sourced from AmbientCG and tracked in `THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt`.
- The default DEM terrain imagery overlay is not bundled. It is generated from the GSI seamless photo tile endpoint `https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg`, with repository-local notes in `THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt`.
- NuGet packages and other runtime dependencies keep their own upstream licenses. Review the exact versions you ship before redistributing binaries or vendored assets.
