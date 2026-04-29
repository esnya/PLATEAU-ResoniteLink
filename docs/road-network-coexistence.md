# Road Network Coexistence

This document defines the Issue #131 coexistence and precedence policy between generated road-network output and higher-detail transport-related output. It builds on [road-network-boundary.md](road-network-boundary.md) and does not implement road-network generation.

## Current Execution Surface

The current import pipeline emits package-scoped `ImportedObjectUnit` values before any Resonite target emission. Each unit is scoped by source file, normalized package name, LOD level, and matched mesh code when available.

The current object-unit optimization path is target-neutral:

- `StreamingImportedSceneSource` groups projected city objects by source file and LOD.
- `IImportedObjectUnitOptimizer` transforms that stream before the target sink receives it.
- `CompositeImportedObjectUnitOptimizer` applies registered optimizers in order.
- The registered optimizer today normalizes dynamic material UV metadata; it does not perform road-network coexistence filtering.

That optimizer seam is the correct place to add executable coexistence policy once generated road-network units have coverage metadata. Until that exists, this document is the policy contract and must not imply that runtime suppression already happens.

## Transport Detail Classes

Road-network coexistence only compares generated road-network units against detailed transport-related output in road space.

Road packages are:

- `tran`
- `rwy`
- `squr`
- `trk`

`wwy` is path-like and may share road-family materials, but it is not a road package and does not suppress generated road-network units by default.

Detailed transport-related output is source CityGML geometry from road packages that represents the same road-space area at an equal or higher source-detail level than the generated road-network unit. Examples include road surface geometry, road markings, roadside structures, railway or track surfaces, and square/open-space transportation surfaces when they overlap the generated unit's road-space footprint.

## Precedence Policy

Detailed transport-related output has precedence over generated road-network output for the overlapping road-space unit.

The policy is:

- If a generated road-network unit and detailed road-package output cover the same road-space unit, emit the detailed output and suppress the generated road-network unit.
- If the overlap is partial, apply the policy to the whole generated road-network unit unless a later accepted design introduces sub-unit splitting.
- If no detailed road-package output overlaps the generated road-network unit, emit the generated road-network unit.
- If the only overlapping output is non-road package output, keep both outputs unless a later package-specific policy says otherwise.
- If the overlap cannot be evaluated because coverage metadata is missing, keep the generated road-network unit and report the missing metadata instead of silently suppressing it.

Suppression removes the generated road-network unit and all generated visual geometry, generated markings, generated debug geometry, and generated network primitives owned by that unit. It does not remove source CityGML geometry.

## Merge Policy

The default merge behavior is no merge.

Generated road-network units and detailed transport-related output either coexist as separate units or the generated road-network unit is suppressed. Do not combine source CityGML transport geometry and generated road-network primitives into a single output unit unless a later change introduces an explicit graph-wide or sub-unit merge design with provenance retention.

Connectivity views may be derived for analysis, but they must not change ownership. Suppression and operator reporting remain traceable to the generated road-network unit and to the detailed source output that took precedence.

## Comparison Inputs

Coexistence comparison must use target-neutral evidence:

- normalized package name
- source file relative path
- matched mesh code and resolved actual mesh code when available
- stable source object id or generated road-network object key
- selected LOD or source-detail level
- road-space coverage footprint in source or geodetic coordinates
- whether a fact is source-observed or inferred

It must not compare by:

- Resonite slot names
- material asset identities
- live-send batching state
- target hierarchy layout
- generated display names alone

## Operator-Visible Behavior

Operators should be able to predict emitted output from the selected packages and coverage:

- Selecting road packages can produce detailed road-package output.
- Enabling a future road-network generator can produce generated road-network units for road-space areas not covered by detailed road-package output.
- Where both outputs claim the same road-space unit, detailed road-package output wins and the generated unit is suppressed.
- `wwy` output can coexist with generated road-network units by default because it is path-like but outside the road package set.
- Suppression counts should be reported when runtime suppression is implemented. The report should identify the suppressed generated unit, the winning detailed source package, and the source file or object evidence used for the decision.

## Non-Goals

This document does not define:

- road-network generation algorithms
- lane, sidewalk, or intersection inference
- Resonite slot layout
- target material assignment
- graph-wide optimization
- sub-unit splitting
- runtime implementation of suppression before road-network units and coverage metadata exist
