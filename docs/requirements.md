# Requirements

## Product Goal

Bring PLATEAU datasets into Resonite through ResoniteLink.

## First Functional Slice

- The entry point is CLI-first.
- A user can specify at least `dataset` and `mesh-code`.
- The command can generate a deterministic Resonite construction contract for mesh-code-scoped local PLATEAU CityGML across supported extracted `udx/<package>/` datasets, whether the mesh code appears in filenames or nested directories.
- The importer must handle official PLATEAU `udx/<package>/` prefixes used by Unity SDK and official CityGML naming rules, not only buildings.
- That contract must remain reusable by a future live Resonite adapter.
- Local datasets are the first implementation target, but the input model must stay extensible to server-backed data.
- The contract preserves CityGML appearance bindings, including `ParameterizedTexture` mappings for detailed models when the referenced texture assets exist.
- The live path must use official ResoniteLink asset import messages for meshes and textures.
- The live path must be able to stream city objects asynchronously so large mesh-code imports do not require one full in-memory batch before sending.

## Non-Goals For Bootstrap

- A GUI
- Full dataset-wide bulk import optimization
- Machine-specific manual setup

## Acceptance Signals

- The same inputs produce the same construction plan.
- CLI input validation is protected by automated tests.
- A live Resonite import creates visible mesh, material, renderer, and collider data for the requested mesh code.
- Formatting, analyzers, build, and tests run consistently in CI.
