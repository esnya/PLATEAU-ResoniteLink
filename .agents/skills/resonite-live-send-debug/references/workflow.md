# Workflow

Use this reference after `SKILL.md` triggers.

This file is the canonical operator-facing live-send workflow for the repository. Keep the source of truth inside `.agents/skills/resonite-live-send-debug/`; do not depend on tracked `docs/` for the current procedure.

Default document fixtures:

- Use `plateau-20202-matsumoto-shi-2020` with adjacent detailed-building meshes `54372778` and `54372788` unless the task requires a different dataset.
- Switch to Yokohama mesh `53391530` only when the task needs `frn` validation.
- Treat those fixture choices as dataset and mesh selectors, not as a promise about cache paths. Confirm the actual local source path on disk before you inspect files or launch cleanup.

## Preconditions

- Use the bundled helper scripts from Windows when the target ResoniteLink listener is not reachable from WSL via `localhost`.
- If the listener is confirmed reachable on `localhost` from WSL, a WSL-driven live run is valid.
- Bare ResoniteLink accepts WebSocket upgrades only when the `Host` header matches `localhost:<port>`. If the sender runs in WSL while the listener runs on Windows, prefer Windows-side execution unless a bridge preserves an acceptable host header.
- Run the repository verification flow before destructive live runs:

```bash
dotnet restore Plateau.ResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

- During iteration, `dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity minimal -m:1 --disable-build-servers -p:UseSharedCompilation=false --filter "Category!=Slow"` is acceptable for quick non-slow checks, but the full sequence above remains the gate before trusting live results or updating a pull request.
- Confirm the target dataset root exists on disk before cleanup or send steps.
- Treat `-Connections` as the active-lane cap under test. Capture at least one baseline run with `-Connections 1` before comparing higher values.
- Cleanup is destructive: it removes matching dataset roots from the current Resonite session and stops matching live-send CLI processes launched from the same repository.

## Required Skill Artifacts

Expect these bundled files under this skill:

- `tools/ResoniteAdmin/ResoniteAdmin.csproj`

Prefer the bundled skill scripts over any ad hoc repo commands. Those wrappers build the admin utility or CLI binaries on demand, so a separate manual build command is not part of the canonical procedure.
When you use the root dump or cleanup helpers, expect them to rebuild `ResoniteAdmin` on demand, emit build output before the actual dump or cleanup step, and require fresh Windows build output. When the Windows app host is present they launch `ResoniteAdmin.exe`; otherwise they fall back to `dotnet` plus the freshly built `.dll`.

## Direct Headless Launch

When you need a disposable listener, prefer starting a headless session with the bundled wrapper:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\start-headless-session.ps1 -RepoPath C:\path\to\repo -HeadlessPath C:\path\to\Resonite -ResoniteLinkPort <port> -SessionName PlateauHeadlessLive -LogPrefix headless-live"
```

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

For disposable headless validation, prefer this sequence:

1. `start-headless-session.ps1`
2. `dump-root-session.ps1 -Label baseline`
3. `cleanup-session.ps1`
4. `run-live-send.ps1`
5. `dump-root-session.ps1 -Label after-send`
6. `stop-headless-session.ps1`

## Root Dump Capture

Capture a full Root dump from the tracked disposable session:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\dump-root-session.ps1 -RepoPath C:\path\to\repo -Label baseline"
```

Record these values from the wrapper output:

- `Endpoint`
- `OutputPath`
- `Depth`
- `IncludeComponentData`
- `AdminDllPath`
- `AdminDllLastWriteTime`

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

Before each comparison run, remove the dataset root and verify that zero matching roots remain:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset>"
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset> -ListOnly"
```

Continue only if list mode reports zero matching dataset roots within the polling window.

Do not run cleanup automatically at the end of a successful validation by default. Keep the final `DatasetRoot` in place unless the user explicitly requests cleanup or the workflow itself is explicitly destructive.

Launch the send with the bundled wrapper:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath C:\path\to\dataset-root -Dataset <dataset> -MeshCode <mesh> -DemTerrainMode <heightmap|mesh> -Connections 1 -LogPrefix <name> -NoWait"
```

The wrapper returns:

- `ProcessId`
- `StdoutLog`
- `StderrLog`
- `CliDllPath`
- `CliDllLastWriteTime`

Use those values. Do not guess the process ID or log path.

## Comparison And Validation

Keep these inputs constant across mode-sensitive comparison runs:

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

When you need the bundled comparison driver:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -ExpectedSessionId <session-id>"
```

Inspect `stderr` first. If it is non-empty, treat that as the primary failure signal. When `stderr` is empty, take at least two timestamped log samples before concluding that a run stalled.

Canonical log reads after launch:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

## Acceptance Signals

Use these checks to judge whether a live validation run is acceptable:

- Deterministic live payloads across repeated runs with the same dataset, mesh code, mode, source path, listener port, and connection count.
- Bake-scope guardrails that confirm LOD1 mesh bake and LOD2 atlas bake do not merge across unrelated CityGML files.
- Visible imported content under the expected dataset root in the live world.
- A green non-slow local test pass during iteration, followed by the full verification sequence before trusting live results.

## Skill Guardrails

Practical rules that stay local to this skill:

- Wait long enough for UDP `12512` announcements.
- Capture `sessionName`, `sessionID`, and `linkPort`.
- Keep the resolved `linkPort` with the run notes.
- If the listener is absent and a disposable headless install is available, start it with the bundled headless wrapper. Otherwise stop and ask the user to bring Resonite back up.
- For disposable headless sessions, prefer a baseline Root dump before the send and a post-send Root dump after the send.
- Prefer UDP discovery when it yields `sessionID`; otherwise require explicit UI confirmation.
- If UDP and UI identify different sessions, mark the run invalid.
- Before each comparison rerun, rediscover the listener and confirm the same session identity again.
- Do not guess the listener port, process ID, or log path. Use discovery output and wrapper return values.
- Treat the bundled script set under `.agents/skills/resonite-live-send-debug/scripts/` as the complete live-test execution surface for this skill.
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
