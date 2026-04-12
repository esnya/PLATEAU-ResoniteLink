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

- During beta iteration, use `bash scripts/test-fast.sh` for quick non-slow checks between low-conflict changes, but keep `bash scripts/verify-ci.sh` as the only repository-owned gate before trusting live results, pushing, or updating a pull request.

- Confirm the target dataset root exists on disk before cleanup or send steps.
- Build outputs do not need to exist ahead of time; the helper scripts build the CLI or admin utility on demand.
- For live sends, prefer the reliable path `-Connections 1` unless you are explicitly testing the experimental multi-connection mode.
- The cleanup steps below are destructive. They remove matching dataset roots from the current Resonite session and stop matching live-send CLI processes launched from the same repository.
- For this workflow, use the bundled scripts under `skills/resonite-live-send-debug/scripts/` as the operator-facing command surface. The root `scripts/` PowerShell helpers remain lower-level repository utilities and are not the procedural source for live runs.
- If you need a disposable listener and have a local Resonite headless installation, prefer the bundled headless wrapper instead of manually preparing a session in the UI.

## Direct Headless Launch

When you want the repository to bring up its own disposable listener, start a headless session directly from Windows:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\start-headless-session.ps1 -RepoPath C:\path\to\repo -HeadlessPath C:\path\to\Resonite -ResoniteLinkPort <port> -SessionName PlateauHeadlessLive -LogPrefix headless-live"
```

The wrapper generates a temporary headless `Config.json`, launches `Resonite.exe` or `Resonite.dll` with `-HeadlessConfig`, waits for a `World Running` log line, and then verifies that UDP discovery sees the requested `linkPort`.

By default, the wrapper also records the launched disposable session to `runtime/windows/headless/active-session.json` so a later stop command can target the same process without retyping its PID.

Record these values from the wrapper output:

- `ProcessId`
- `SessionName`
- `SessionId`
- `LinkPort`
- `Endpoint`
- `ConfigPath`
- `StdoutLog`
- `StderrLog`
- `StatePath`

When the experiment is over, stop the tracked disposable headless process:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\stop-headless-session.ps1 -RepoPath C:\path\to\repo"
```

If you need to stop a different tracked state file or an explicit PID, pass `-StatePath` or `-ProcessId`.

## Root Dump Capture

For disposable headless sessions, a full Root dump is usually low-noise enough to keep as both the baseline and the post-send world-state artifact. Prefer taking both snapshots against the same tracked session.

Capture a full Root dump from the tracked disposable session:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\dump-root-session.ps1 -RepoPath C:\path\to\repo -Label baseline"
```

The wrapper resolves the endpoint from `runtime/windows/headless/active-session.json` by default. It writes a recursive Root snapshot to `runtime/windows/resonite/root-dumps/` unless you pass `-OutputPath`.
It rebuilds `ResoniteAdmin` on demand, prefers `ResoniteAdmin.exe` when the binary is available, and falls back to `dotnet` plus the `.dll` otherwise.

Record these values from the wrapper output:

- `Endpoint`
- `OutputPath`
- `Depth`
- `IncludeComponentData`
- `AdminDllPath`
- `AdminDllLastWriteTime`

Recommended disposable-headless validation sequence:

1. `start-headless-session.ps1`
2. `dump-root-session.ps1 -Label baseline`
3. `cleanup-session.ps1`
4. `run-live-send.ps1`
5. `dump-root-session.ps1 -Label after-send`
6. `stop-headless-session.ps1`

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
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath C:\path\to\dataset-root -Dataset <dataset> -MeshCode <mesh> -DemTerrainMode <heightmap|mesh> -Connections 1 -LogPrefix <name> -NoWait"
```

The wrapper rebuilds the Windows CLI output before the send unless you explicitly pass `-SkipBuild`. Treat the run as invalid if the wrapper returns an unexpectedly old `CliDllLastWriteTime` or a non-Windows output path.

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

When you need world-state evidence in addition to log comparison, capture a Root dump before the first run and after each observation point.

If you need the bundled comparison driver, use:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -ExpectedSessionId <session-id>"
```

To let the comparison driver bring up and tear down its own disposable headless listener, add `-HeadlessPath`:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -HeadlessPath C:\path\to\Resonite -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -HeadlessSessionName PlateauHeadlessLive"
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
  Keep `bash scripts/test-fast.sh` green during local iteration and `bash scripts/verify-ci.sh` green before treating any live result as trustworthy.

When you stop a run early, stop only the specific launched PID, verify that it exited, then run cleanup again and keep the logs. When a run exits on its own, record the exit code before cleanup.
