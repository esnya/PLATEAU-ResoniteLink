# Workflow

Use this guide after `SKILL.md` triggers.

This file is the single operational guide surface for the repo-local live-send skill. Keep fixture values, environment-dependent choices, comparison worksheets, and version-scoped runtime notes here instead of duplicating them in `SKILL.md`.

## Defaults

- Use `plateau-20202-matsumoto-shi-2020` with meshes `54372778` and `54372788` unless the task needs a different fixture.
- Switch to Yokohama mesh `53391530` only for `frn` or city-furniture validation.
- Treat those defaults as selectors, not as a promise about cache paths. Confirm the actual resolved local source path before cleanup or send.
- Before destructive steps, confirm that the requested dataset root exists locally and that the requested mesh is supported by current local evidence or fixtures.

## Environment Selection

- Use bundled helper scripts instead of ad hoc commands.
- Prefer PowerShell 7 helper execution with `pwsh.exe -NoProfile -File ...`.
- Avoid Windows PowerShell 5.1 for the helper scripts when PowerShell 7 is available. The current helper surface relies on behaviors such as `ConvertFrom-Json -Depth` that are smoother on PowerShell 7, and current Windows execution-policy defaults can block direct `.ps1` invocation.
- Run helpers from Windows when the target listener is not reachable from WSL through `localhost`.
- A WSL-driven sender is valid when the listener is same-host and actual `localhost` reachability from WSL has been confirmed.
- If a reverse proxy or bridge rewrites the route to an acceptable host for the listener, an IP-based path can be valid when reachability and session identity are both confirmed.
- Decide by observed reachability and observed session identity. Do not hardcode a Windows-only or WSL-only rule into the workflow.
- Root dumps and destructive cleanup use the bundled repo-local session tool, not the official REPL prompt loop.
- Keep explicit `ws://host:port/` endpoints in automation paths whenever the session target is already known.
- In sandboxed Codex environments, the helpers can require elevated execution because they restore or build the CLI or session tool. If a helper fails on .NET first-use or permission setup, rerun the helper with sandbox escalation instead of replacing it with an ad hoc command sequence.

## Agent Guardrails

- Re-run listener discovery before each comparison rerun and record `sessionName`, `sessionID`, and `linkPort`.
- Do not guess listener port, process ID, log path, or session identity. Use discovery output, helper stdout, and CLI logs.
- Treat cleanup as destructive. It can remove dataset roots, stop matching live-send CLI processes, and delete local runtime artifacts.
- Keep the final successful `DatasetRoot` in place unless the user explicitly requests cleanup.
- Inspect `stderr` before interpreting `stdout`. When `stderr` is empty, take at least two timestamped log reads before calling a run stalled.

## Fixed Run Worksheet

Keep these facts fixed or explicitly updated between comparison runs:

- dataset
- mesh code
- local source path
- listener port
- session name
- session id
- connection count
- mode
- log prefix
- launched PID
- launched CLI binary path and last write time

For disposable headless validation, prefer this operator sequence:

1. `start-headless-session.ps1`
2. `dump-root-session.ps1 -Label baseline`
3. `cleanup-session.ps1`
4. `run-live-send.ps1`
5. `dump-root-session.ps1 -Label after-send`
6. `stop-headless-session.ps1`

Use `run-live-send-monitored.ps1` instead of `run-live-send.ps1` when the main operator need is to wait through a long run while surfacing the latest `import` / `live` lines, fail fast on obvious log errors, or kill the process when a memory cap is exceeded.

For the fixed Matsumoto `54372778 -> 54372788` base/append validation on `19001`, run the helpers directly in this order:

1. `cleanup-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Dataset plateau-20202-matsumoto-shi-2020`
2. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-baseappend-baseline`
3. `run-live-send.ps1 -RepoPath <repo> -ResoniteLinkPort 19001 -LocalSourcePath <archive> -Dataset plateau-20202-matsumoto-shi-2020 -MeshCode 54372778 -DemTerrainMode heightmap -Connections 1 -LogPrefix matsumoto-base-heightmap-19001`
4. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-base-heightmap-after-send`
5. `run-live-send.ps1 -RepoPath <repo> -ResoniteLinkPort 19001 -LocalSourcePath <archive> -Dataset plateau-20202-matsumoto-shi-2020 -MeshCode 54372788 -DemTerrainMode heightmap -Connections 1 -LogPrefix matsumoto-append-heightmap-19001`
6. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-append-heightmap-after-send`

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
- Prefer `SetFromLocalBounds` for the probe step. In the currently observed workspace session, do not use `SetFromLocalBoundsPrecise` as an evaluation path because it returned unit bounds instead of usable occupancy values. Treat `SetFromLocalBounds` as the primary bounds-capture path unless the user explicitly wants a world-space comparison.
- Do not assume that a global-bounds helper is the correct default. Record the exact callable path that was used and keep `SetFromGlobalBounds` as an explicit alternative, not the baseline procedure.
- After the local-bounds update runs, capture the BoxCollider state together with the slot transform. Treat the `Size` and `Offset` values as slot-local occupancy and combine them with the slot transform only when a world-space interpretation is needed.
- Standard procedure: remove the BoxCollider probe after readback so the inspected world returns to its pre-probe state. If a probe is intentionally left in place for manual follow-up, record that deviation explicitly in the run notes.
- The currently observed workspace session contains intentionally retained BoxCollider probes from exploratory validation. Treat those as temporary evidence, not as the baseline cleanup policy.
- If the session does not expose a usable local-bounds update path, stop and report that the current session did not prove an automatic BoxCollider-based bounds readback workflow.
- If reflection yields no useful component type or callable surface, fall back to the root-dump evidence and treat the BoxCollider bounds path as unverified rather than filling in guessed type or method names.

## Bounds Regression Checklist

- Identity check:
  record dataset, mesh code, slot tag, slot name, and whether the inspected slot is DEM, atlas bake, mesh bake, or another emitted category.
- Structural check:
  confirm the expected slot exists under the expected dataset branch, and confirm whether the expected renderer and collider components are present before adding the probe.
- Local occupancy check:
  record BoxCollider `Size` and `Offset` after `SetFromLocalBounds`.
- Placement check:
  record the slot transform together with the BoxCollider local values so later comparisons can distinguish slot misplacement from geometry extent changes.
- Rotation check:
  record the slot rotation whenever the slot is not identity-aligned. Bounds regressions on rotated slots cannot be interpreted from collider values alone.
- Category comparison:
  compare like with like. DEM should be compared against DEM, atlas-baked building slots against atlas-baked building slots, and mesh-baked slots against mesh-baked slots.
- Expected-shape check:
  for DEM, watch for near-zero thickness, implausibly large vertical extent, or sudden XY shrink/stretch.
  for buildings, watch for sudden collapse to unit scale, large offset drift relative to the slot origin, or a major swap between horizontal footprint and height.
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

- `tools/ResoniteSessionTool/ResoniteSessionTool.csproj`

The helper scripts rebuild the thin session tool or CLI binaries on demand. Fresh Windows build output is part of the expected execution path for dump and cleanup helpers.

## Read-Only Inspection

- Treat the JSON written by `dump-root-session.ps1` as the primary read artifact.
- `jq` is optional convenience for post-dump inspection only. Do not make cleanup convergence or slot selection depend on `jq`.
- Example:
  `jq '.Root.Children[] | { id: .ID, name: .Name.Value }' runtime/windows/resonite/root-dumps/<dump>.json`

## Public Helper Commands

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

`scripts/windows-build-tools.ps1` remains an internal shared helper and is not part of the operator-facing command surface.
