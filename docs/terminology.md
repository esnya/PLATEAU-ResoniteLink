# Terminology

This document freezes the canonical terminology and rename boundaries for the pending terminology migration tracked by `#126` and `#127`.

## Goals

- stop adding new ambiguous names before the migration lands
- distinguish canonical terms from grandfathered legacy surfaces
- keep the later rename cut atomic and alias-free

## Rules

- Treat English documentation as the canonical wording source.
- Do not introduce compatibility aliases during the migration. Rename directories, filenames, namespaces, types, docs, and CLI surfaces in the same cut when a term actually changes.
- Do not add new uses of ambiguous bare terms in code, docs, tests, or issue text after this document lands.

## Canonical Terms

| Term | Canonical meaning | Notes |
| --- | --- | --- |
| `mesh` | geometry mesh data | Do not use bare `mesh` for PLATEAU mesh-code selection. |
| `mesh code` | PLATEAU geographic mesh selector | Existing public `--mesh-code` stays grandfathered until the migration cut. |
| `source` | input location or retrieval source | Use for dataset/archive/raster origin paths and URLs only. |
| `origin` | provenance or geodetic reference origin | Prefer qualified forms such as `object origin`, `file origin`, and `geodetic origin`. |
| `build` | legacy import/execution term only | New names should prefer `import`, `compose`, `emit`, or another phase-specific verb. |
| `bootstrap` | qualified setup phase label only | Always qualify it, for example `discovery bootstrap` or `scene bootstrap`. |
| `profile` | configurable behavior or budget profile | Do not use it as a generic grouping word for unrelated folders or namespaces. |
| `PLATEAU package` | PLATEAU dataset package such as `bldg`, `dem`, or `tran` | Always qualify `package` when the meaning could collide with dependency packages. |
| `NuGet package` | dependency package | Always qualify `package` when the meaning could collide with PLATEAU packages. |

## Grandfathered Legacy Surfaces

These names are still allowed until the dedicated rename migration lands, but they should not be copied into new APIs or docs:

- CLI `build` command and related `SceneBuild*` types
- `PlateauMeshCode`
- `ResoniteLocalOrigin`
- existing provenance-oriented `Source*` field and type names
- `Tests.Profiles` folder and namespace grouping

## New Usage Bans

- Do not use bare `mesh` when the meaning is PLATEAU mesh-code selection.
- Do not use `source` for provenance when `origin` is the intended meaning.
- Do not use bare `bootstrap` without a phase qualifier.
- Do not introduce new pipeline or grouping names that use `profile` without a real configurable-profile meaning.
- Do not use bare `package` when the intended meaning is either PLATEAU package or dependency package.
- Do not introduce new import-phase names that rely on `build` unless they extend an existing grandfathered surface.

## Migration Boundary

`#126` defines terminology and the rename map only. It does not rename code.

`#127` will apply the approved migration atomically:

- update code, directories, namespaces, docs, and CLI terms in one cut
- remove legacy names instead of keeping aliases
- preserve behavior while changing terminology
