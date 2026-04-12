# Live Testing

## Purpose

Use this workflow when you need machine-level confirmation that a local PLATEAU dataset can be streamed into a real Resonite session through ResoniteLink.

This document is the canonical live-send workflow for the repository and the only procedural source for live-send runs. The bundled scripts under `skills/resonite-live-send-debug/scripts/` are the operator-facing entrypoint for this workflow; the root `scripts/` PowerShell helpers are lower-level repository utilities that support the same workflow.

## Preconditions

- Live testing is currently Windows-only because the bundled helper scripts target a Windows Resonite session and PowerShell.
- Run the repository verification flow before any destructive live run:

```bash
bash scripts/verify-ci.sh
```

- Confirm the target dataset root exists on disk before cleanup or send steps.
- Build outputs do not need to exist ahead of time; the helper scripts build the CLI or admin utility on demand.
- The cleanup steps below are destructive. They remove matching dataset roots from the current Resonite session and stop matching live-send CLI processes launched from the same repository.
- For this workflow, use the bundled scripts under `skills/resonite-live-send-debug/scripts/` as the operator-facing command surface. The root `scripts/` PowerShell helpers remain lower-level repository utilities and are not the procedural source for live runs.

## Listener Discovery

Use the bundled discovery script to capture ResoniteLink UDP announcements from port `12512`:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\discover-session.ps1 -TimeoutSeconds 20 -MaxAnnouncements 5"
```

Record these values before every comparison run:

- `sessionName`
- `sessionID`
- `linkPort`

Rules:

- Prefer UDP discovery when it yields `sessionID`.
- If discovery and the in-game UI identify different sessions, treat the run as invalid.
- Re-run discovery before each comparison rerun and confirm the same session identity again.

## Cleanup And Send

Before each comparison run, remove the dataset root and verify that zero matching roots remain with the bundled cleanup wrapper:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset>"
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset> -ListOnly"
```

Continue only if list mode reports zero matching dataset roots within the polling window.

Then launch the send from Windows with the bundled wrapper:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath C:\path\to\dataset-root -Dataset <dataset> -MeshCode <mesh> -DemTerrainMode <heightmap|mesh> -Connections 8 -LogPrefix <name> -NoWait"
```

The wrapper returns:

- `ProcessId`
- `StdoutLog`
- `StderrLog`
- `CliDllPath`
- `CliDllLastWriteTime`

Use those exact values. Do not guess the process ID or log path.

The helper scripts preserve per-run stdout and stderr logs by default. Keep those logs with distinct names for later comparison.

## Comparison And Validation

For mode-sensitive investigations, keep these inputs constant across runs:

- dataset
- mesh code
- local source path
- listener port
- connection count

Preferred sequence:

1. `heightmap`
2. cleanup
3. `mesh`
4. cleanup
5. `heightmap`

If you need the bundled comparison driver, use:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -ExpectedSessionId <session-id>"
```

Inspect `stderr` first. If it is non-empty, treat that as the primary failure signal. When `stderr` is empty, take at least two timestamped log samples before concluding that a run stalled.

Canonical log reads:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

## Acceptance Signals

Map live validation back to `docs/requirements.md` with these checks:

- Deterministic live payloads:
  Run the same dataset, mesh code, mode, source path, listener port, and connection count more than once.
  Compare the generated log sequence, observed dataset root structure, and any reusable asset hierarchy for unexplained differences.
- Visible imported content:
  Confirm the expected dataset root appears in the live world and that the target subtree contains the expected mesh, material, renderer, and collider data.
- CI-equivalent validation:
  Keep `bash scripts/verify-ci.sh` green before treating any live result as trustworthy.

When you stop a run early, stop only the specific launched PID, verify that it exited, then run cleanup again and keep the logs. When a run exits on its own, record the exit code before cleanup.
