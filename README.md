# Plateau.ResoniteLink

<img width="2560" height="1440" alt="2026-04-08 03 02 41" src="https://github.com/user-attachments/assets/7dac58c7-8855-4362-855d-f12e884dc05e" />

Plateau.ResoniteLink is a .NET 10 CLI for streaming [PLATEAU](https://www.mlit.go.jp/plateau/) CityGML datasets into Resonite through [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink). Import behavior and terminology stay aligned with [PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/). GitHub Releases are the canonical changelog, and each `vX.Y.Z` release publishes a framework-dependent CLI asset named `Plateau.ResoniteLink-cli-vX.Y.Z.zip`.

## Scope

- Stream local PLATEAU datasets or explicit remote CityGML ZIP/7z archives into a running ResoniteLink listener.
- Preserve deterministic mesh/material ordering, keep `ParameterizedTexture` appearance data where present, and fall back to bundled default materials when source textures are missing.
- Build dataset and mesh-code branches incrementally so imported content can begin appearing in Resonite before the full request completes.

## Runtime And Prerequisites

- Target runtime: .NET SDK 10. Release assets also require .NET 10.
- A running ResoniteLink listener reachable by `--resonitelink-port` or `--resonitelink-url` is required.
- Live adapter asset import currently uses `ImportMesh(ImportMeshRawData)` for meshes and `ImportTexture(ImportTexture2DFile)` for textures.

## Quick Start

Restore dependencies:

```bash
dotnet restore Plateau.ResoniteLink.sln
```

For Codex Cloud or similar ephemeral environments, use:

```bash
./scripts/setup-codex-cloud.sh
```

That script bootstraps .NET 10 when needed, then runs the repository verification flow.

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

`--resonitelink-port` or `--resonitelink-url` is required. `--source remote` requires a direct `.zip` or `.7z` CityGML archive URL and does not perform built-in dataset search. Validate formatting, analyzers, build, and tests with:

```bash
bash scripts/verify-ci.sh
```

## Further Reading

- Product requirements: [docs/requirements.md](docs/requirements.md)

## License And Provenance

- The repository source code is licensed under [MIT](LICENSE).
- Imported PLATEAU datasets are not re-licensed by this repository. Review each dataset's README, metadata, download page, and rights notices before importing, redistributing, or publishing derived content.
- The [PLATEAU Site Policy](https://www.mlit.go.jp/plateau/site-policy/) is only the portal-level default and does not override dataset-specific terms.
- The [PLATEAU Start Guide](https://www.mlit.go.jp/plateau/start-guide/) notes that dataset terms vary by source, including licenses such as PDL 1.0, CC BY 4.0, ODC BY, or ODbL.
- PLATEAU SDK for Unity is a separate upstream MIT-licensed project. A local copy of that license is tracked in `THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt`.
- Bundled default material textures under `src/Plateau.ResoniteLink.Cli/Assets/DefaultMaterials/` are sourced from AmbientCG and tracked in `THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt`.
- The default DEM terrain imagery overlay is not bundled. It is generated from the GSI seamless photo tile endpoint `https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg`, with repository-local notes in `THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt`.
- NuGet packages and other runtime dependencies keep their own upstream licenses. Review the exact versions you ship before redistributing binaries or vendored assets.
