# Terminology

This document is the current-main rename map for repository concepts. It is the source of truth for issue #165 and later terminology migrations.

## Rules

- Prefer PLATEAU SDK for Unity terminology when naming dataset, package, mesh-code, tile, and adapter concepts.
- Rename a concept in one cut across directories, filenames, namespaces, CLI help, docs, tests, and resources.
- Do not add compatibility aliases for renamed internal concepts. Keep old names only at explicit external input, serialized data, or historical documentation boundaries.
- Use target-neutral names above adapter edges. Resonite-specific terms belong under Resonite target, transport, or live-send adapters.

## Canonical Map

| Current or ambiguous term | Canonical term | Applies to | Migration rule |
| --- | --- | --- | --- |
| `tile` when it means a Japanese standard mesh code | `mesh-code` | CLI, docs, source discovery, dataset selection | Rename user-facing text and internal identifiers. Keep `--tile` only as a deprecated external CLI alias until the migration cut removes aliases. |
| `mesh` when it means PLATEAU mesh-code area | `mesh-code` or `mesh-code bounds` | discovery, filtering, source grouping | Do not use `mesh` for geography. Reserve `mesh` for renderable geometry payloads. |
| `source` by itself | `CityGML source`, `GeoTIFF source`, `terrain texture source`, or `dataset source` | CLI, import requests, source adapters | Qualify the source kind unless the local type already names the boundary. |
| `origin` for geographic coordinates | `geo origin` or `geodetic origin` | projection and local-coordinate conversion | Use `local origin` only for the target/local scene anchor after projection. |
| `origin` for object/source ownership | `source unit` or `source file` | parsed objects, bake scope, provenance | Use `source unit` for logical ownership and `source file` for physical CityGML file identity. |
| `build` for import preparation | `prepare`, `plan`, `bake`, or `emit` | import pipeline and target execution | Pick the phase-specific verb. Do not use `build` as a generic lifecycle phase. |
| `bootstrap` for long-lived import state | `discovery`, `parsed`, `prepared`, or `plan` | setup/discovery and result models | Keep `bootstrap` only for pre-streaming setup that creates fixed scene/session context. |
| `profile` for runtime policy | `policy`, `preset`, or `material profile` | import options, material defaults, budget settings | Use `profile` only for named reusable material/budget presets, not for arbitrary policy objects. |
| `package` | `PLATEAU package` or `package name` | `bldg`, `tran`, `dem`, and other UDX package concepts | Keep `package` for PLATEAU/UDX package names. Do not reuse it for file archives or dependency packages in import code. |
| `adapter` | `source adapter`, `target adapter`, or `transport adapter` | boundary implementations | Qualify the side of the boundary. |

## Grandfathered Surfaces

These names may remain until a dedicated migration removes them:

- Deprecated CLI aliases and error messages that exist only to guide users from old flags to canonical flags.
- Historical issue, PR, changelog, and release-note text.
- Third-party names, upstream schema names, package IDs, and file paths whose spelling is externally defined.
- Serialized or wire names required by ResoniteLink, CityGML, GeoTIFF, or other external formats.

## Cut Boundary for #165

Issue #165 should migrate repository-owned names only. It must not change external schema names, third-party asset paths, or user data formats unless the change is an explicit CLI alias removal.

The first migration cut should:

- remove deprecated internal uses of `tile` for mesh-code concepts;
- split ambiguous `source` identifiers where the code crosses CityGML, GeoTIFF, terrain texture, or dataset boundaries;
- replace generic `origin` identifiers with `geo origin`, `geodetic origin`, `local origin`, `source unit`, or `source file`;
- rename generic `build` and misplaced `bootstrap` concepts to phase-specific names;
- keep namespaces aligned with the final directory ownership after each rename.
