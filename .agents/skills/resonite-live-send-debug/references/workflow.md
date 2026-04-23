# Workflow

Use this guide after `SKILL.md` triggers.

This file is the single operational guide surface for the repo-local live-send skill. Keep fixture values, comparison worksheets, and version-scoped runtime notes here instead of duplicating them in `SKILL.md`.

## Fixture Catalog

Use these fixtures deliberately. They are not interchangeable.

- Lightweight default:
  Matsumoto `plateau-20202-matsumoto-shi-2020`
  with `54372778` and `54372788`.
  Use this for fast end-to-end checks and the lightweight standard whole test.
  It does not cover `LOD3` buildings, `frn`, `tran`, or similarly rich package mixes.
- Standard GeoTIFF whole-test fixture:
  Higashimurayama `plateau-13213-higashimurayama-shi-2020`
  with base `53395325` and append `53395326`,
  using local CityGML archive `source-archive-5039b16c4b1c.zip`
  and local GeoTIFF archive `source-ortho-cc68652cc45c.7z`.
  Use this for reliable GeoTIFF-backed mixed-mode whole tests.
  It is effectively `LOD2`-only and does not cover `LOD3`, `frn`, or `tran`.
- Expanded-coverage building/material fixture:
  Yokohama mesh `53391530`.
  Use this when richer building/material stress is needed.
  It includes `LOD3` buildings, heavier texture cases such as standalone 4K textures, and package coverage including `frn`, `tran`, and `brid`.
- Expanded-coverage non-building / texture-strategy fixture:
  Sendai `plateau-04100-sendai-shi-2024` with mesh `57403710`.
  Use this when broader non-building coverage is needed.
  It has no `LOD3` buildings, but it does include `frn` / `tran` at `LOD3` and many uniform or single-color texture cases.

## Defaults

- Default fixture selection has three layers:
  - lightweight default:
    Matsumoto
  - standard whole test:
    Matsumoto mixed-mode plus Higashimurayama mixed-mode with GeoTIFF
  - expanded coverage:
    Yokohama or Sendai only when broader LOD/package/material coverage is needed
- Record which fixture and test class were chosen in the run notes before send.
- Treat fixture names as selectors, not as a promise about cache paths. Confirm the actual resolved local source path before send.
- Before destructive steps, confirm that the requested dataset root exists locally and that the requested mesh is supported by current fixture evidence.

## Agent Guardrails

- Re-run listener discovery before each comparison rerun and record `sessionName`, `sessionID`, and `linkPort`.
- Do not guess listener port, process ID, log path, or session identity. Use discovery output, direct command stdout, and CLI logs.
- Treat `dump-slot` and `remove-slot` as thin primitives. Do not encode dataset-root, shared-assets, or common-material naming semantics into the tool surface.
- Treat slot removal as destructive. It can remove live content from the current world.
- Keep the final successful `DatasetRoot` in place unless the user explicitly requests removal.
- Do not parallelize cleanup, root-dump, and live-send commands against the same session. Run removal, post-removal verification dumps, and each send serially so base-state evidence stays valid.
- Inspect `stderr` before interpreting `stdout`. When `stderr` is empty, take at least two timestamped log reads before calling a run stalled.
- Use direct `dotnet` commands as the public operator surface. Do not recreate `.ps1` wrappers, a project-based session tool, or cross-environment bridge guidance.
- `dump-slot --root-child-name` and `remove-slot --root-child-name` resolve exact direct children under `Root` only. Zero matches must fail. Multiple matches must fail without mutating the world.
- When ResoniteLink uses `localhost`, run sender, listener, and headless in the same environment. Mention that assumption in the run notes instead of trying to bridge environments inside this skill.
- Do not clean other dataset roots by default. Default cleanup only when rerunning the same dataset root and the run is supposed to start from a clean base.
- For standard whole tests, do not collect dump-at-every-step by default. The required dump is the final post-append slot dump. Pre-send or post-removal dumps are escalation artifacts for cleanup-sensitive reruns or contamination checks.
- Before any resend that is supposed to start from a clean base, capture a post-removal pre-send root dump and treat the run as contaminated if that dump still shows stale dataset content.

## Headless Launcher Path Guide

- Treat `--headless-path` as an explicit launcher root or launcher file when provided. Do not silently replace a bad path with an unrelated local copy.
- The resolver only checks the given file or directory plus these nearby candidates:
  `Resonite.dll`, `Resonite.exe`, `Headless/Resonite.dll`, and `Headless/Resonite.exe`.
- If `--headless-path` is omitted on Windows, the tool only auto-checks the standard Steam install root:
  `C:\Program Files (x86)\Steam\steamapps\common\Resonite`
- If a separate headless-only install exists on the machine, point `--headless-path` at that install root or directly at its `Resonite.exe` / `Resonite.dll`.
- If the configured path does not contain one of the accepted launcher candidates, stop and report the missing launcher instead of guessing another machine-local directory.
- Prefer the installed app tree over copied sandboxes when headless startup depends on version-matched runtime files, writable metadata caches, or machine-local auth/config.

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

## Artifact Expectations By Test Class

- Standard whole test:
  keep command logs for base and append, and capture the final post-append slot dump.
- Cleanup-sensitive rerun:
  add cleanup proof and pre-send dump(s) for the same dataset root.
- Expanded-coverage exploratory run:
  collect extra dumps only when the question actually needs them.

For disposable headless validation, prefer this operator sequence:

1. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- start-headless --runtime-root <headless-runtime> --state-path <headless-runtime>/active-session.json --resonitelink-port 19001`
2. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot --runtime-root <headless-runtime> --output <repo>/runtime/windows/resonite/root-dumps/baseline.json`
3. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:19001/ --root-child-name "PLATEAU plateau-20202-matsumoto-shi-2020"`
4. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/post-removal-pre-send.json`
5. `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset plateau-20202-matsumoto-shi-2020 --mesh-code 54372778 --citygml-source <archive> --work-root <repo>/runtime/windows/resonite --dem-terrain-mode heightmap --resonitelink-port 19001 --resonitelink-connections 1`
6. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/after-send.json`
7. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- stop-headless --runtime-root <headless-runtime> --state-path <headless-runtime>/active-session.json`

## Standard Whole Tests

Use the next two worksheets as the default full live verification set before reaching for Yokohama or Sendai.

### Matsumoto Mixed-Mode Whole Test

Use Matsumoto for the lightweight mixed-mode baseline.
The canonical direction is `base=mesh`, `append=heightmap`.
The inverse direction is valid when explicitly needed, but it is not the default worksheet.

1. If and only if the world already contains `PLATEAU plateau-20202-matsumoto-shi-2020` and this run must start from a clean Matsumoto base:
   `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:19001/ --root-child-name "PLATEAU plateau-20202-matsumoto-shi-2020"`
2. If step 1 ran:
   `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/matsumoto-post-removal-pre-send.json`
3. `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset plateau-20202-matsumoto-shi-2020 --mesh-code 54372778 --citygml-source <archive> --work-root <repo>/runtime/windows/resonite --dem-terrain-mode mesh --resonitelink-port 19001 --resonitelink-connections 1`
4. `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset plateau-20202-matsumoto-shi-2020 --mesh-code 54372788 --citygml-source <archive> --work-root <repo>/runtime/windows/resonite --dem-terrain-mode heightmap --resonitelink-port 19001 --resonitelink-connections 1`
5. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/matsumoto-append-heightmap-after-send.json`

### Higashimurayama Mixed-Mode Whole Test

Use Higashimurayama for the standard GeoTIFF-backed mixed-mode whole test.
The canonical direction is `base=heightmap`, `append=mesh`.
The inverse direction is valid when explicitly needed, but it is not the default worksheet.

1. If and only if the world already contains `PLATEAU plateau-13213-higashimurayama-shi-2020` and this run must start from a clean Higashimurayama base:
   `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:19001/ --root-child-name "PLATEAU plateau-13213-higashimurayama-shi-2020"`
2. If step 1 ran:
   `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/higashimurayama-post-removal-pre-send.json`
3. `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset plateau-13213-higashimurayama-shi-2020 --mesh-code 53395325 --packages dem,bldg --citygml-source <repo>/runtime/windows/resonite/plateau-13213-higashimurayama-shi-2020/source-archive-5039b16c4b1c.zip --geotiff-source <repo>/runtime/windows/resonite/plateau-13213-higashimurayama-shi-2020/source-ortho-cc68652cc45c.7z --work-root <repo>/runtime/windows/resonite --dem-terrain-mode heightmap --resonitelink-port 19001 --resonitelink-connections 1`
4. `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset plateau-13213-higashimurayama-shi-2020 --mesh-code 53395326 --packages dem,bldg --citygml-source <repo>/runtime/windows/resonite/plateau-13213-higashimurayama-shi-2020/source-archive-5039b16c4b1c.zip --geotiff-source <repo>/runtime/windows/resonite/plateau-13213-higashimurayama-shi-2020/source-ortho-cc68652cc45c.7z --work-root <repo>/runtime/windows/resonite --dem-terrain-mode mesh --resonitelink-port 19001 --resonitelink-connections 1`
5. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/higashimurayama-append-mesh-after-send.json`

## Cleanup Escalation

- Only clean the dataset root you are about to rerun from a clean base.
- Do not clean unrelated dataset roots by default.
- If the world contains additional stale roots such as `PLATEAU Shared Assets` or `Common Materials`, inspect a root dump, choose the exact slot IDs or exact root-child names intentionally, and remove them one at a time with `remove-slot`.
- Do not treat those names as a guaranteed stable API.

## Component Type Discovery

- When a live inspection needs an exact component type name, prefer ResoniteLink reflection over guesswork.
- Primary path: connect with the local ResoniteLink library or official REPL helper and query `GetComponentTypeList` first, then `GetComponentDefinition` for candidates.
- Use category queries when possible. Reserve `GetComponentTypeList("*")` for cases where narrower categories are unknown, and record when the session returns an empty list.
- If reflection is unavailable or returns no useful data, inspect existing `componentType` values in slot dumps as a fallback evidence source.
- Distinguish UI labels from runtime type strings. A picker label like `Texture2D Metadata` is not sufficient proof of the exact `AddComponent` type name.

## BoxCollider Bounds Inspection

- Use this procedure when the user wants to estimate rendered occupancy or compare likely position or mesh regressions by attaching a BoxCollider probe to an imported slot and reading back the resulting bounds.
- Start from a successful live send plus a post-send slot dump. Do not attempt the bounds inspection on a failed or partial run.
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
- If reflection yields no useful component type or callable surface, fall back to the slot-dump evidence and treat the BoxCollider bounds path as unverified rather than filling in guessed type or method names.

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

Expect these tracked files under this skill:

- `tools/session-tool.cs`
- `src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj`

Direct `dotnet` execution rebuilds the session tool script or CLI on demand. Fresh local build output is part of the expected execution path for dump, removal, headless, and live-send commands.

## Read-Only Inspection

- Treat the JSON written by `dump-slot` as the primary read artifact.
- `jq` is optional convenience for post-dump inspection only. Do not make cleanup convergence or slot selection depend on `jq`.
- Example:
  `jq '.Slot.children[] | { id: .id, name: .name.value }' <dump>.json`

## Direct Command Surface

- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- discover-session`
  Capture live ResoniteLink announcements from UDP `12512`.
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- start-headless --runtime-root <headless-runtime> --state-path <headless-runtime>/active-session.json [--headless-path <headless>] --resonitelink-port <port>`
  Launch a disposable headless session directly and verify its announced ResoniteLink port. The launcher is resolved from the given file or directory plus a few nearby standard candidates, and `.dll` launchers run through the environment's `dotnet` command.
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- stop-headless --runtime-root <headless-runtime> --state-path <headless-runtime>/active-session.json`
  Stop the tracked headless PID launched for the experiment, or an explicit PID.
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:<port>/ --slot-id Root --output <dump>.json`
  Capture a recursive slot snapshot from the tracked or explicitly addressed session.
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:<port>/ --root-child-name "PLATEAU plateau-20202-matsumoto-shi-2020" --depth 1`
  Resolve an exact direct child under `Root`, then dump that slot.
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:<port>/ --slot-id <slot-id>`
  Remove one explicitly addressed slot.
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:<port>/ --root-child-name "PLATEAU plateau-20202-matsumoto-shi-2020"`
  Resolve one exact direct child under `Root`, then remove that slot. This is convenience for operator workflow, not a semantic cleanup API.
- `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset <dataset> --mesh-code <mesh> --citygml-source <archive-or-udx> --work-root <repo>/runtime/windows/resonite --dem-terrain-mode <heightmap|mesh> --resonitelink-port <port> --resonitelink-connections <n>`
  Launch one direct live send with explicit logs under `runtime/windows/resonite`.
