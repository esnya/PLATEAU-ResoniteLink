# Plateau.ResoniteLink

Plateau.ResoniteLink is a .NET 10 project for bringing PLATEAU datasets into Resonite. The first implemented slice is CLI-first and turns a dataset plus mesh-code selection into a deterministic Resonite construction plan and a live ResoniteLink import path for local or online `bldg` CityGML data.

`PLATEAU-SDK-for-UNITY` is the implementation reference for import semantics and terminology.

## Usage

Restore dependencies:

```bash
dotnet restore Plateau.ResoniteLink.sln
```

Generate a construction plan from a local dataset root:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 53394525 \
  --source local \
  --input /path/to/plateau
```

The CLI writes a Resonite construction plan JSON under `artifacts/<os>/resonite/<dataset>/<mesh-code>/`.
By default, build outputs and CLI artifacts are separated per host OS. On Linux/WSL, the default artifact root is `artifacts/linux/resonite/`; on Windows, it is `artifacts/windows/resonite/`.
The current v1 importer reads local `bldg` CityGML, preserves deterministic submesh/material ordering, and emits live-ready mesh and material payloads.

Fetch an official PLATEAU CityGML ZIP online through the default CKAN catalog flow:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source server
```

`--source server` uses the official `search.ckan.jp` catalog by default, discovers a matching CityGML ZIP resource, downloads it into `artifacts/<os>/resonite/server-cache/`, extracts it, and then runs the same deterministic local importer on the extracted dataset root. `--server-url` can override the catalog base URI or point directly to a ZIP archive URL.

Build live into Resonite through ResoniteLink from Windows:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source server \
  --resonitelink-port <port>
```

The live path connects to `ws://localhost:<port>/`, imports mesh and texture assets through official ResoniteLink import messages, creates dataset and mesh-code slots, and then attaches `StaticMesh`, `StaticTexture2D`, `MeshRenderer`, `PBS_Metallic`, and `MeshCollider` components. JSON artifact output is kept as a local record.

The current live adapter uses `ImportMesh(ImportMeshRawData)` for meshes and `ImportTexture(ImportTexture2DFile)` for textures, because the current ResoniteLink runtime returns a usable mesh asset URL on the raw-data path.

Validate formatting, analyzers, and tests:

```bash
dotnet format Plateau.ResoniteLink.sln --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore
```
