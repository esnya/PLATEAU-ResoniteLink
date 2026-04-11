# Workflow

Use this reference after `SKILL.md` triggers.

The canonical repository workflow lives in [docs/live-testing.md](../../../docs/live-testing.md). This file intentionally does not repeat cleanup, send, or comparison procedure steps.

Default document fixtures:

- Use `plateau-20202-matsumoto-shi-2020` with adjacent detailed-building meshes `54372778` and `54372788` unless the task requires a different dataset.
- Switch to Yokohama mesh `53391530` only when the task needs `frn` validation.
- Treat those fixture choices as dataset and mesh selectors, not as a promise about cache paths. Confirm the actual local source path on disk before you inspect files or launch cleanup.

## Required Repo Artifacts

Expect these files under the workspace root:

- `scripts/ResoniteAdmin/ResoniteAdmin.csproj`

Prefer the bundled skill scripts over any ad hoc repo scripts. Those wrappers build the admin utility or CLI binaries on demand, so a separate manual build command is not part of the canonical procedure.

## Skill Guardrails

Practical rules that stay local to this skill:

- Wait long enough for UDP `12512` announcements.
- Capture `sessionName`, `sessionID`, and `linkPort`.
- Keep the resolved `linkPort` with the run notes.
- If the listener is absent, stop and ask the user to bring Resonite back up.
- Prefer UDP discovery when it yields `sessionID`; otherwise require explicit UI confirmation.
- If UDP and UI identify different sessions, mark the run invalid.
- Before each comparison rerun, rediscover the listener and confirm the same session identity again.
- Do not guess the listener port, process ID, or log path. Use discovery output and wrapper return values.
- Treat the root `scripts/` live-send helpers as lower-level repository utilities. The operator-facing surface for this skill is the bundled script set under `skills/resonite-live-send-debug/scripts/`.
- Warning: cleanup is destructive. It removes dataset roots from the live world, stops matching live-send CLI processes launched from the same repo, and clears local runtime artifacts.

The send wrapper returns a PowerShell object with these properties:

- `ProcessId`
- `StdoutLog`
- `StderrLog`
- `CliDllPath`
- `CliDllLastWriteTime`

Use those values. Do not guess the log path or process id.

Canonical log reads after a launch:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

From WSL:

```bash
tail -n 40 /mnt/c/path/to/stdout.log
tail -n 40 /mnt/c/path/to/stderr.log
```

## Script Inventory

- `scripts/discover-session.ps1`
  Capture live ResoniteLink announcements from UDP `12512`.
- `scripts/cleanup-session.ps1`
  Remove dataset roots from the live world, stop leftover CLI processes, and clear local runtime artifacts.
- `scripts/run-live-send.ps1`
  Launch one Windows-side live send with explicit logs.
- `scripts/compare-modes.ps1`
  Run the standard `heightmap -> mesh -> heightmap` comparison with cleanup between runs.
