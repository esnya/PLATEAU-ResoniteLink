# Workflow

Use this reference after `SKILL.md` triggers.

Default document fixtures:

- Use `plateau-20202-matsumoto-shi-2020` with adjacent detailed-building meshes `54372778` and `54372788` unless the task requires a different dataset.
- Switch to Yokohama mesh `53391530` only when the task needs `frn` validation, because the local cache provides `udx/frn/53391530_frn_6697_*` there.

## Required Repo Artifacts

Expect these files under the workspace root:

- `scripts/ResoniteAdmin/ResoniteAdmin.csproj`

Prefer the bundled skill scripts over any ad hoc repo scripts.

If the admin utility or CLI binaries are missing, build them before running:

```bash
cmd.exe /c "cd /d C:\path\to\repo && dotnet.exe build scripts\ResoniteAdmin\ResoniteAdmin.csproj -c Release"
cmd.exe /c "cd /d C:\path\to\repo && dotnet.exe build src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj -c Release"
```

## Listener Discovery

Use this workflow as the source of truth for live listener discovery.

Practical rules:

- Wait long enough for UDP `12512` announcements.
- Capture `sessionName`, `sessionID`, and `linkPort`.
- Keep the resolved `linkPort` with the run notes.
- If the listener is absent, stop and ask the user to bring Resonite back up.
- Prefer UDP discovery when it yields `sessionID`; otherwise require explicit UI confirmation.
- If UDP and UI identify different sessions, mark the run invalid.
- Before each comparison rerun, rediscover the listener and confirm the same session identity again.

## World Cleanup

Warning: this step is destructive. Removing the dataset root destroys live-world results in the current Resonite session, and the bundled cleanup script also stops matching live-send CLI processes launched from the same repo. Do not run it against a session the user wants to preserve.

Before each comparison run:

1. Remove the dataset root from the current world with the admin utility.
2. Re-run the admin utility in list mode every 2 seconds.
3. Continue only if it reports zero matching dataset roots within 20 seconds.
4. If it does not converge, invalidate the run.

Example pattern:

```bash
cmd.exe /c "cd /d C:\path\to\repo && dotnet.exe artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.dll ws://localhost:<port>/ <dataset>"
cmd.exe /c "cd /d C:\path\to\repo && dotnet.exe artifacts\build\windows\bin\ResoniteAdmin\Release\net10.0\ResoniteAdmin.dll ws://localhost:<port>/ <dataset> --list-only"
```

Treat the test as invalid if the verification does not reach zero within the polling window.

After world cleanup, clear local artifacts with the bundled cleanup script:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset>"
```

## Running the Send

Prefer the bundled wrapper because it standardizes log capture:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath C:\path\to\dataset-root -Dataset <dataset> -MeshCode <mesh> -DemTerrainMode <heightmap|mesh> -Connections 8 -LogPrefix <name> -NoWait"
```

The wrapper returns a PowerShell object with these properties:

- `ProcessId`
- `StdoutLog`
- `StderrLog`
- `CliDllPath`
- `CliDllLastWriteTime`

Use those values. Do not guess the log path or process id.

Canonical log reads:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

From WSL:

```bash
tail -n 40 /mnt/c/path/to/stdout.log
tail -n 40 /mnt/c/path/to/stderr.log
```

## Comparison Pattern

For mode-sensitive bugs, use a fixed sequence:

1. `heightmap`
2. cleanup
3. `mesh`
4. cleanup
5. `heightmap`

Keep these constant across all runs:

- dataset
- mesh code
- source path
- listener port
- connection count

If you need the standard sequence quickly, use the bundled comparison driver:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -ExpectedSessionId <session-id>"
```

## Interpreting Logs

Check `stderr` first. If it is non-empty, treat that as the primary failure signal.

When `stderr` is empty:

- tail `stdout`
- wait a measured interval and tail again
- record the observation timestamps
- note the last `live` and `import` lines
- note whether the launched PID is still alive
- distinguish these cases:
  - parse or materialization still progressing
  - live send still progressing
  - no new output at all

Do not conclude "silent hang after Sent" unless you have confirmed that the process is still alive, neither `import` nor `live` logs are advancing across multiple timestamped samples, and the target world subtree is not advancing across multiple timestamped snapshots either.

## Ending the Experiment

When you stop a run early:

1. stop only the specific CLI process you launched
2. verify that the targeted PID is no longer alive
3. remove the dataset root from the world
4. verify zero matching dataset roots remain
5. keep the logs with their unique names

When a run exits on its own:

1. record the exit code for the launched PID
2. remove the dataset root unless the user explicitly asked to keep it
3. verify zero matching dataset roots remain
4. record whether root-only cleanup may still leave orphan contamination
5. if the run failed or was interrupted, treat structure-level conclusions as provisional unless an orphan audit was performed

Record whether the run was:

- valid
- invalid due to stale world state
- invalid due to listener loss
- invalid due to user interruption
