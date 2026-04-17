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
