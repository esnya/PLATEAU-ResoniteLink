# Live Testing

## Purpose

Use this workflow when you need machine-level confirmation that a local PLATEAU dataset can be streamed into a real Resonite session through ResoniteLink.

This document is the operator-facing and human-facing live-send workflow reference for the repository. The bundled scripts under `.agents/skills/resonite-live-send-debug/scripts/` are the command surface for this workflow.
Put Coding Agent-specific execution heuristics in `.agents/skills/resonite-live-send-debug/SKILL.md` so the procedural workflow stays readable for operators.

## Preconditions

- The current bundled helper scripts are Windows-oriented. Use them from Windows when the target ResoniteLink listener is not reachable from WSL via `localhost`.
- If the listener is running inside WSL and is confirmed reachable on `localhost` there, a WSL-driven live run is valid. That exception is about listener reachability, not about requiring the listener itself to be a Windows process.
- Bare ResoniteLink accepts WebSocket upgrades only when the `Host` header matches `localhost:<port>`. If the sender runs in WSL while the listener runs on Windows, that default check usually forces the sender process onto Windows unless a reverse proxy or equivalent bridge rewrites the request so the listener still sees an acceptable host.
- When a reverse proxy or another transport bridge preserves reachability and satisfies the listener's host check, an IP-based route can still be valid. Decide the sender environment from the actual network path and the observed listener behavior instead of assuming Windows-only or WSL-only execution.
- Run the repository verification flow before any destructive live run:

```bash
dotnet restore Plateau.ResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

- During beta iteration, use `dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity minimal -m:1 --disable-build-servers -p:UseSharedCompilation=false --filter "Category!=Slow"` for quick non-slow checks between low-conflict changes, but keep the full verification command sequence above as the gate before trusting live results, pushing, or updating a pull request.

- Confirm the target dataset root exists on disk before cleanup or send steps.
- Build outputs do not need to exist ahead of time; the helper scripts build the CLI or admin utility on demand.
- Treat `-Connections` as the active-lane cap under test. Capture at least one baseline run with `-Connections 1`, then compare it with the intended multi-connection value when validating invariant behavior.
- The cleanup steps below are destructive. They remove matching dataset roots from the current Resonite session and stop matching live-send CLI processes launched from the same repository.
- For this workflow, use the bundled scripts under `.agents/skills/resonite-live-send-debug/scripts/` as the complete command surface.
- If you need a disposable listener and have a local Resonite headless installation, prefer the bundled headless wrapper instead of manually preparing a session in the UI.

## Recommended Fixture Parameters

Unless the task requires a different target, start with the Matsumoto dataset `plateau-20202-matsumoto-shi-2020` and adjacent detailed-building meshes `54372778` and `54372788`. These are recommended starting-point parameters for document-backed reproductions and comparisons, not procedural requirements.

## Direct Headless Launch

When you want the repository to bring up its own disposable listener, prefer starting a headless session directly from Windows with the current helper scripts. If your disposable listener is launched inside WSL and exposed on `localhost`, that path is valid as long as the listener is reachable from the sender environment.

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\start-headless-session.ps1 -RepoPath C:\path\to\repo -HeadlessPath C:\path\to\Resonite -ResoniteLinkPort <port> -SessionName PlateauHeadlessLive -LogPrefix headless-live"
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
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\stop-headless-session.ps1 -RepoPath C:\path\to\repo"
```

If you need to stop a different tracked state file or an explicit PID, pass `-StatePath` or `-ProcessId`.

## Root Dump Capture

For disposable headless sessions, a full Root dump is usually low-noise enough to keep as both the baseline and the post-send world-state artifact. Prefer taking both snapshots against the same tracked session.

Capture a full Root dump from the tracked disposable session:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\dump-root-session.ps1 -RepoPath C:\path\to\repo -Label baseline"
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
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\discover-session.ps1 -TimeoutSeconds 20 -MaxAnnouncements 5"
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
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset>"
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset> -ListOnly"
```

Continue only if list mode reports zero matching dataset roots within the polling window.

Do not run cleanup automatically at the end of a successful validation by default. End-of-run cleanup is opt-in and should happen only when the user explicitly requests cleanup or when the workflow itself is explicitly destructive, such as disposable headless teardown. Keep the dataset root in place after the final successful run unless there is a stated reason to remove it, because the retained `DatasetRoot` is the default artifact for final visual inspection.

Then launch the send with the bundled wrapper. Use Windows for the current bundled wrapper flow when the listener is not reachable from WSL via `localhost`. A WSL-driven send is acceptable only when the listener is confirmed reachable on `localhost` from WSL:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath C:\path\to\dataset-root -Dataset <dataset> -MeshCode <mesh> -DemTerrainMode <heightmap|mesh> -Connections 1 -LogPrefix <name> -NoWait"
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

For connection-count guardrails, repeat the same mode with `-Connections 1` and the target higher value, then compare the resulting logs and world state before changing any other input.

When you need world-state evidence in addition to log comparison, capture a Root dump before the first run and after each observation point.

If you need the bundled comparison driver, use:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -ExpectedSessionId <session-id>"
```

To let the comparison driver bring up and tear down its own disposable headless listener, add `-HeadlessPath`:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -HeadlessPath C:\path\to\Resonite -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -HeadlessSessionName PlateauHeadlessLive"
```

Inspect `stderr` first. If it is non-empty, treat that as the primary failure signal. When `stderr` is empty, take at least two timestamped log samples before concluding that a run stalled.

Canonical log reads:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

## Acceptance Signals

Use these checks to judge whether a live validation run is acceptable:

- Deterministic live payloads:
  Run the same dataset, mesh code, mode, source path, listener port, and connection count more than once.
  Compare the generated log sequence, observed dataset root structure, and any reusable asset hierarchy for unexplained differences.
- Bake-scope guardrails:
  Confirm that LOD1 mesh bake and LOD2 atlas bake do not merge across unrelated CityGML files, and that reordering input city objects does not change baked material or mesh payload contents beyond deterministic batch identity suffixes.
- Visible imported content:
  Confirm the expected dataset root appears in the live world and that the target subtree contains the expected mesh, material, renderer, and collider data.
- CI-equivalent validation:
  Keep the non-slow `dotnet test ... --filter "Category!=Slow"` command green during local iteration and rerun the full verification command sequence before treating any live result as trustworthy.

When you stop a run early, stop only the specific launched PID, verify that it exited, then run cleanup again and keep the logs. When a run exits on its own, record the exit code before any optional cleanup. Do not treat final cleanup as implicit.
