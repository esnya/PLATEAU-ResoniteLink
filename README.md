# Plateau.ResoniteLink

Plateau.ResoniteLink is a .NET 10 project for bringing [PLATEAU](https://www.mlit.go.jp/plateau/) datasets into Resonite through [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink). It streams CityGML-derived city objects directly into Resonite so imported content can begin appearing as it is processed.

Import behavior and terminology are guided by [PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/).

Release tags use the `vX.Y.Z` format. Build outputs derive `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` from those tags, emitting numeric assembly versions without the `v` prefix.

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
  --resonitelink-port <port>
```

`--resonitelink-port` or `--resonitelink-url` is required. The optional `--work-root` defaults to `runtime/<os>/resonite/` and is used only for generated live assets and the remote download cache. The optional `--packages` accepts a comma-separated list of official PLATEAU `udx/<package>/` names; when omitted, the CLI defaults to `dem,bldg,brid,frn,tran,rwy,trk,tun,ubld,unf,veg`. The option names follow PLATEAU SDK for Unity where practical: `--local-source-path` matches `DatasetSourceConfigLocal.LocalSourcePath`, and `--server-url` matches `DatasetSourceConfigRemote.ServerUrl`. The current importer reads mesh-code-scoped local CityGML across official PLATEAU `udx/<package>/` prefixes, preserves deterministic submesh/material ordering, carries `ParameterizedTexture` appearance data from detailed models into live-ready mesh and material payloads, and streams city objects without holding the full live build in memory.

Import an official PLATEAU CityGML ZIP online through the default CKAN catalog flow:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source remote \
  --resonitelink-port <port>
```

`--source remote` uses the official `search.ckan.jp` catalog by default, discovers a matching CityGML ZIP resource, downloads it into `runtime/<os>/resonite/cache/remote/`, extracts it, and then runs the same local importer on the extracted local source path. `--server-url` can override the catalog base URI or point directly to a ZIP archive URL.

To reuse already-downloaded data, switch back to local import and point `--local-source-path` at either the extracted dataset root or an ancestor directory under `runtime/<os>/resonite/cache/remote/`. The importer resolves the nearest descendant that contains `udx/`, so a path such as `runtime/<os>/resonite/cache/remote/tokyo23ku/533944/` is valid even when the extracted dataset root is nested one level below it.

Build live into Resonite through ResoniteLink from Windows:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source remote \
  --resonitelink-port <port>
```

The live path connects to `ws://localhost:<port>/`, imports mesh and texture assets through official ResoniteLink import messages, creates dataset and mesh-code slots, attaches a dataset-level `License` component with PLATEAU attribution text, and then adds `StaticMesh`, `StaticTexture2D`, `MeshRenderer`, `PBS_Metallic`, and `MeshCollider` components. City objects are sent sequentially so large mesh-code imports do not require a full in-memory batch before visible live output.

The current live adapter uses `ImportMesh(ImportMeshRawData)` for meshes and `ImportTexture(ImportTexture2DFile)` for textures, because the current ResoniteLink runtime returns a usable mesh asset URL on the raw-data path.

Validate formatting, analyzers, and tests:

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release
```
