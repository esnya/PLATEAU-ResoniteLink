# Workflow

Use this reference after `SKILL.md` triggers.

This file stays agent-facing on purpose. Use [docs/live-testing.md](../../../docs/live-testing.md) for the repository's current operator workflow, command examples, and validation sequence.

## Defaults

- Use `plateau-20202-matsumoto-shi-2020` with meshes `54372778` and `54372788` unless the task needs a different fixture.
- Switch to Yokohama mesh `53391530` only for `frn` validation.
- Treat those defaults as selectors, not as a promise about cache paths. Confirm the actual resolved local source path before cleanup or send.

## Agent Guardrails

- Prefer the bundled scripts under `.agents/skills/resonite-live-send-debug/scripts/` instead of ad hoc commands.
- Use Windows-side wrappers when WSL cannot reach the listener through `localhost`.
- Re-run listener discovery before each comparison rerun and keep `sessionName`, `sessionID`, and `linkPort` in the run notes.
- Do not guess the listener port, process ID, log path, or session identity. Use discovery output and wrapper return values.
- Treat cleanup as destructive. It can remove dataset roots, stop matching live-send CLI processes, and delete local runtime artifacts.
- Keep the final `DatasetRoot` in place after a successful validation unless the user explicitly requests cleanup.
- Inspect `stderr` before interpreting `stdout`. When `stderr` is empty, take at least two timestamped log reads before calling a run stalled.

## Required Artifacts

Expect these bundled files under this skill:

- `tools/ResoniteAdmin/ResoniteAdmin.csproj`

The wrappers rebuild the admin utility or CLI binaries on demand. Fresh Windows build output is part of the expected execution path for dump and cleanup helpers.

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
- `scripts/compare-modes.ps1`
  Run the standard `heightmap -> mesh -> heightmap` comparison with cleanup between runs.
- `scripts/check-matsumoto-base-append-heightmap-19001.ps1`
  Run the fixed Matsumoto base/append validation for `54372778 -> 54372788` on port `19001` in `heightmap` mode, with root dumps before the base send, after the base send, and after the append send.
