# Road Network Boundary

This document fixes the representation boundary for road-network support before coexistence behavior is implemented. It is the specification cut for Issue #130. Issue #131 owns duplicate suppression, precedence, and merge behavior between this output and higher-detail transport-related output.

## Observed Baseline

The current import pipeline discovers CityGML source files under `udx/<package>/...`, normalizes PLATEAU package names, and streams package-scoped `ImportedObjectUnit` values into target-neutral construction geometry before the Resonite target layer emits anything.

Road-related package handling is already package-based:

- `tran`, `rwy`, `squr`, and `trk` are `RoadPackageNames`.
- `wwy` is path-like, but not a road package.
- Default material selection maps road and path-like packages to the bundled road material family.
- The CityGML reader currently projects polygon and triangle surfaces. It does not treat PLATEAU transportation files as an existing topological road graph.
- Transportation surfaces already have target-neutral terrain alignment behavior before Resonite emission.

The PLATEAU SDK for Unity describes road-network generation as a derived process from imported road models: it estimates road structure, lanes, sidewalks, intersections, and lane connections from road mesh geometry, and warns that automatically generated networks are estimates. This repository should preserve that separation: source CityGML road geometry is input evidence, not itself the road-network graph.

References:

- PLATEAU SDK for Unity road-network manual: https://project-plateau.github.io/PLATEAU-SDK-for-Unity/manual/RoadNetwork.html
- PLATEAU RoadNetwork Generator overview: https://project-plateau.github.io/PLATEAU-RoadNetwork-Generator/index.html

## Decision

Road network is represented as a derived transport abstraction.

It is not a raw source graph:

- PLATEAU CityGML transportation package content can include road surfaces, markings, and higher-detail structures, but the current importer only has a reliable source-file and city-object surface stream.
- The SDK-aligned behavior is generation from road model evidence, not direct reuse of an authoritative road topology.

It is not a Resonite-specific target model:

- Coexistence decisions must run before target emission so they can be tested without ResoniteLink.
- Geometry, provenance, coverage, and generated network semantics must remain target-neutral until the target adapter converts them.

The abstraction must live on the importing/application side of the boundary and carry enough provenance for later policy:

- source package name
- source file relative path
- matched mesh code and resolved actual mesh code when available
- source object id or stable object key
- selected LOD or inferred source-detail level
- road-space coverage footprint in source coordinates or geodetic coordinates
- generated road-network elements owned by that source evidence

## Source Abstraction

The source abstraction is `RoadNetworkSource` in concept, even if the first implementation names it differently. It is a read-side contract over selected road-package CityGML evidence, not a target output contract.

It should be created from the same discovery window as current CityGML import:

- It only sees requested mesh codes and requested packages.
- It only consumes normalized road packages: `tran`, `rwy`, `squr`, and `trk`.
- It may use existing parsed road surfaces, road marking detection, object ids, source file descriptors, LOD selection, and CRS conversion.
- It must not pull Resonite slot names, material asset identities, target batching state, or coexistence decisions into the source model.
- It must preserve source provenance even when generated road-network elements are merged or simplified.

The source abstraction may contain inferred facts, but they must be labeled as inferred. Examples include lane count, sidewalk presence, road-axis shape, intersection membership, and road-space coverage. Source facts such as package name, source file, object id, LOD, CRS, and mesh code must remain distinguishable from inferred facts.

## Output Unit

The road-network output unit is a road-space unit, not a whole dataset and not an individual target slot.

The minimum unit of ownership is one generated road-network unit derived from one source road object or one stable source-object group when the source object is split for mesh-area filtering. That unit owns:

- its source provenance
- its road-space coverage footprint
- its generated network primitives, such as roads, ways, lanes, sidewalks, intersections, or tracks
- any generated visual geometry, markings, or debug geometry attached to that generated network unit

If the implementation projects this through existing `ImportedObjectUnit`, the descriptor should remain source-file/package/LOD scoped, and each generated `ImportedCityObject` must keep a stable road-network object key and source provenance. If a dedicated road-network unit type is added later, it should still be convertible into target-neutral construction geometry without target-specific fields.

Do not make the output unit a global road graph unless a later change explicitly introduces graph-wide optimization. Global connectivity may be computed as a derived view, but ownership and coexistence must still be traceable to road-space units.

## Ownership Boundary

The road-network generator owns:

- deriving target-neutral road-network units from road-package source evidence
- preserving source provenance and inferred-vs-source labels
- producing deterministic object keys and deterministic ordering
- emitting enough coverage metadata for coexistence policy

The existing CityGML source discovery and parsing pipeline owns:

- source file enumeration
- package normalization
- mesh-code selection
- CRS parsing and validation
- source object parsing

The target adapters own:

- converting target-neutral generated geometry into Resonite construction data
- material and asset emission
- target-specific slot hierarchy and live-send behavior

Issue #131 owns:

- deciding whether detailed transport output suppresses, replaces, or coexists with road-network units
- comparing road-space coverage between generated road-network units and detailed transport-related output
- documenting the operator-visible precedence behavior

## #131 Build Path

#131 should consume this boundary instead of redefining it.

The recommended build path is:

1. Add executable metadata for road-space coverage if the road-network unit is implemented in code.
2. Run coexistence before target emission, preferably in the source composition or object-unit optimization path.
3. Compare detailed transport-related output and road-network units by coverage and provenance, not by Resonite slot names.
4. Apply policy to whole road-space units unless a later accepted design introduces sub-unit splitting.
5. Keep detailed-output precedence and merge rules in #131 documentation and tests, not in this #130 boundary document.

Open questions for #131:

- Which detailed transport packages and object classes suppress road-network units?
- Whether detailed output should always win, or only when it reaches a configured detail level or coverage threshold.
- Whether markings generated from the road-network unit are suppressed together with the network unit.
- How to surface suppressed-unit counts in CLI progress and datasource summaries.

## Non-Goals

This document does not implement:

- road-network generation
- road-network export
- coexistence or precedence rules
- duplicate suppression
- Resonite-specific slot layout
- a compatibility alias for future renamed concepts
