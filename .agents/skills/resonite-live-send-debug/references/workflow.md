# Workflow

Use this reference after `SKILL.md` triggers.

This file stays agent-facing on purpose. Use it as supplemental notes after `SKILL.md`; do not depend on removed tracked live-testing documents.

## Defaults

- Use `plateau-20202-matsumoto-shi-2020` with meshes `54372778` and `54372788` unless the task needs a different fixture.
- Switch to Yokohama mesh `53391530` only for `frn` validation.
- Treat those defaults as selectors, not as a promise about cache paths. Confirm the actual resolved local source path before cleanup or send.

## Agent Guardrails

- Prefer the bundled helper scripts under `.agents/skills/resonite-live-send-debug/scripts/` instead of ad hoc commands.
- Use Windows-side helper scripts when WSL cannot reach the listener through `localhost`.
- Re-run listener discovery before each comparison rerun and keep `sessionName`, `sessionID`, and `linkPort` in the run notes.
- Do not guess the listener port, process ID, log path, or session identity. Use discovery output, helper stdout, and CLI logs.
- Treat cleanup as destructive. It can remove dataset roots, stop matching live-send CLI processes, and delete local runtime artifacts.
- Keep the final `DatasetRoot` in place after a successful validation unless the user explicitly requests cleanup.
- Inspect `stderr` before interpreting `stdout`. When `stderr` is empty, take at least two timestamped log reads before calling a run stalled.

## Component Type Discovery

- When a live inspection needs an exact component type name, prefer ResoniteLink reflection over guesswork.
- Primary path: connect with the local ResoniteLink library or official REPL helper and query `GetComponentTypeList` first, then `GetComponentDefinition` for candidates.
- Use category queries when possible. Reserve `GetComponentTypeList("*")` for cases where narrower categories are unknown, and record when the session returns an empty list.
- If reflection is unavailable or returns no useful data, inspect existing `componentType` values in root dumps as a fallback evidence source.
- Distinguish UI labels from runtime type strings. A picker label like `Texture2D Metadata` is not sufficient proof of the exact `AddComponent` type name.

## BoxCollider Bounds Inspection

- Use this procedure when the user wants to estimate rendered occupancy or compare likely position or mesh regressions by attaching a BoxCollider probe to an imported slot and reading back the resulting bounds.
- Start from a successful live send plus a post-send root dump. Do not attempt the bounds inspection on a failed or partial run.
- First confirm the target slot already exists in the dump and record its identity: dataset root name, slot name, slot tag, slot transform, and any existing collider evidence already attached to that slot.
- In the currently observed Matsumoto runs, imported slots carried `[FrooxEngine]FrooxEngine.MeshCollider`, and imported slots were also able to accept `[FrooxEngine]FrooxEngine.BoxCollider` probes. Treat that as current evidence, not as a timeless guarantee.
- If the target slot already has a collider shape that is sufficient for the regression question, prefer dump-based evidence first. Only mutate the world when the user explicitly wants a BoxCollider-based bounds probe.
- When a BoxCollider probe is required, connect through the official REPL or an equivalent reflection-capable ResoniteLink client and resolve the accepted BoxCollider runtime type before adding anything:
  1. Query `GetComponentTypeList` with the narrowest collider-oriented filter the session supports.
  2. Run `GetComponentDefinition` on the candidate runtime types and record the exact type string that the session accepts.
- After the runtime type is confirmed, add the BoxCollider probe to the target slot and inspect the callable or member surface exposed by the session for bounds-derived update paths.
- Prefer `SetFromLocalBounds` or `SetFromLocalBoundsPrecise` for the probe step. Treat those as the primary bounds-capture path unless the user explicitly wants a world-space comparison.
- Do not assume that a global-bounds helper is the correct default. Record the exact callable path that was used and keep `SetFromGlobalBounds` as an explicit alternative, not the baseline procedure.
- After the local-bounds update runs, capture the BoxCollider state together with the slot transform. Treat the `Size` and `Offset` values as slot-local occupancy and combine them with the slot transform only when a world-space interpretation is needed.
- Standard procedure: remove the BoxCollider probe after readback so the inspected world returns to its pre-probe state. If a probe is intentionally left in place for manual follow-up, record that deviation explicitly in the run notes.
- The currently observed workspace session contains intentionally retained BoxCollider probes from exploratory validation. Treat those as temporary evidence, not as the baseline cleanup policy.
- If the session does not expose a usable local-bounds update path, stop and report that the current session did not prove an automatic BoxCollider-based bounds readback workflow.
- If reflection yields no useful component type or callable surface, fall back to the root-dump evidence and treat the BoxCollider bounds path as unverified rather than filling in guessed type or method names.

## Bounds Regression Checklist

- Use the checklist below to decide what to inspect after a BoxCollider readback. Do not reduce the procedure to `Size` alone.
- Identity check:
  record dataset, mesh code, slot tag, slot name, and whether the inspected slot is DEM, atlas bake, mesh bake, or another emitted category.
- Structural check:
  confirm the expected slot exists under the expected dataset branch, and confirm whether the expected renderer and collider components are present before adding the probe.
- Local occupancy check:
  record BoxCollider `Size` and `Offset` after `SetFromLocalBounds` or `SetFromLocalBoundsPrecise`.
- Placement check:
  record the slot transform together with the BoxCollider local values so later comparisons can distinguish slot misplacement from geometry extent changes.
- Rotation check:
  record the slot rotation whenever the slot is not identity-aligned. Bounds regressions on rotated slots cannot be interpreted from collider values alone.
- Category comparison:
  compare like with like. DEM should be compared against DEM, atlas-baked building slots against atlas-baked building slots, and mesh-baked slots against mesh-baked slots.
- Expected-shape check:
  for DEM, watch for near-zero thickness, implausibly large vertical extent, or sudden XY shrink/stretch.
  for buildings, watch for sudden collapse to unit-scale, large offset drift relative to the slot origin, or a major swap between horizontal footprint and height.
- Run-to-run comparison:
  compare the same slot tag across runs first. Only fall back to name-based matching when the tag is unavailable.
- Cleanup check:
  after recording the readback, remove the probe component unless the run is intentionally being preserved for manual inspection.

## Matsumoto Reference Values

- Treat the values in this section as current reference data for Matsumoto live checks, not as strict golden numbers.
- The purpose of these numbers is to catch extreme unintended changes such as collapse to unit scale, axis swaps, sudden offset explosions, or category-mismatched geometry. They are comparison seeds, not pass/fail thresholds.
- Prefer comparing the same slot tag across runs. Do not compare different mesh codes or different emitted categories as if they shared one expected shape.

- Reference sample A:
  dataset `plateau-20202-matsumoto-shi-2020`
  mesh code `54372778`
  category `DEM`
  slot tag `54372778|dem|none|udx_dem_543727_dem_6697_55_op_gml_dem_d0d95755_3366_4fa2_8c49_9c304fb295ce`
  slot position `{"x":3934.2598,"y":612.1313,"z":2310.8608}`
  slot rotation `{"x":0.7071068,"y":0.0,"z":0.0,"w":0.7071068}`
  local BoxCollider offset `{"x":0.0,"y":0.0,"z":0.0}`
  local BoxCollider size `{"x":1123.9403,"y":924.78,"z":0.0}`
  interpretation:
  this is a thin DEM sheet after `SetFromLocalBounds`; one axis collapsing near zero is expected here, while sudden thickness growth or XY collapse is suspicious.

- Reference sample B:
  dataset `plateau-20202-matsumoto-shi-2020`
  mesh code `54372788`
  category `AtlasBake`
  slot tag `54372788|bldg|2|atlasbake:54372788:bldg:06927625:61717c92b772:2:0003`
  slot position `{"x":-574.12836,"y":590.0462,"z":-467.71707}`
  slot rotation `{"x":0.0,"y":0.0,"z":0.0,"w":1.0}`
  local BoxCollider offset `{"x":434.89987,"y":13.178907,"z":463.00485}`
  local BoxCollider size `{"x":869.79974,"y":26.357815,"z":926.0097}`
  interpretation:
  this is a broad, low-height atlas-baked building aggregate; collapse toward unit scale, large origin drift, or a major height-vs-footprint swap is suspicious.

- Artifact path for the current reference snapshot:
  `runtime/windows/resonite/root-dumps/workspace-matsumoto-local-bounds-eval-20260418-180506.json`

## RawOutput Readback Limits

- In the currently observed combination of `Resonite 2026.4.16.1327` and `ResoniteLink 0.13.1.0`, `RawOutput` members on metadata components did not produce readable values through Link even when the target asset reference was set correctly and the component was polled again later.
- The confirmed examples were `[FrooxEngine]FrooxEngine.Texture2DAssetMetadata` and `[FrooxEngine]FrooxEngine.BitmapAssetMetadata` attached to a `StaticTexture2D`.
- Treat this as version-specific evidence, not a timeless rule. If either Resonite or ResoniteLink changes, re-verify before relying on the limitation.

## Required Artifacts

Expect these bundled files under this skill:

- `tools/ResoniteAdmin/ResoniteAdmin.csproj`

The helper scripts rebuild the admin utility or CLI binaries on demand. Fresh Windows build output is part of the expected execution path for dump and cleanup helpers.

## Script Inventory

- `scripts/discover-session.ps1`
  Capture live ResoniteLink announcements from UDP `12512`.
- `scripts/start-headless-session.ps1`
  Launch a disposable Windows headless session directly and verify its announced ResoniteLink port.
- `scripts/stop-headless-session.ps1`
  Stop the tracked headless PID launched for the experiment, or an explicit PID.
- `scripts/dump-root-session.ps1`
  Capture a recursive Root snapshot from the tracked or explicitly addressed session.
- `scripts/cleanup-session.ps1`
  Remove dataset roots from the live world, stop leftover CLI processes, and clear local runtime artifacts.
- `scripts/run-live-send.ps1`
  Launch one Windows-side live send with explicit logs.
- `scripts/windows-build-tools.ps1`
  Resolve Windows-side `dotnet` and shared ResoniteAdmin build paths for the other helper scripts.
