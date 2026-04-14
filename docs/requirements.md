# Requirements

## Product Goal

Bring PLATEAU datasets into Resonite through ResoniteLink.

## First Functional Slice

- The entry point is CLI-first.
- A user can specify at least `dataset` and `mesh-code`.
- The command can deterministically stream mesh-code-scoped local PLATEAU CityGML across supported extracted `udx/<package>/` datasets, preferring official PLATEAU CityGML filename conventions and falling back to mesh-code directory segments when local extracts omit the code from filenames.
- The importer must handle official PLATEAU `udx/<package>/` prefixes used by Unity SDK and official CityGML naming rules, not only buildings.
- Local datasets are the first implementation target, but the input model must stay extensible to remote data and keep Unity-aligned source naming.
- The importer preserves CityGML appearance bindings, including `ParameterizedTexture` mappings for detailed models when the referenced texture assets exist.
- The live path must use official ResoniteLink asset import messages for meshes and textures.
- The live path must be able to stream city objects asynchronously so large mesh-code imports do not require one full in-memory batch before sending.
- LOD1 mesh bake and LOD2 atlas bake must use dependency-only keyed batching. The bake key must include CityGML scope, package, LOD, and bake-policy context so batching does not depend on arrival order and does not merge across unrelated source files.

## Non-Goals For Bootstrap

- A GUI
- Full dataset-wide bulk import optimization
- Machine-specific manual setup

## Acceptance Signals

- The same inputs produce the same live mesh/material payloads.
- Reordering city objects within the same import does not change baked payload contents beyond deterministic batch identity suffixes.
- CLI input validation is protected by automated tests.
- A live Resonite import creates visible mesh, material, renderer, and collider data for the requested mesh code.
- Formatting, analyzers, build, and tests run consistently in CI.
