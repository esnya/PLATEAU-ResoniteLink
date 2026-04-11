---
name: resonite-live-send-debug
description: Run and debug PLATEAU-ResoniteLink live-send reproductions against a real Windows ResoniteLink session. Use when the user wants actual machine-level validation instead of simulated tests, including listener discovery, DatasetRoot cleanup between runs, alternating `heightmap` and `mesh` comparisons, log capture, and inspection of the generated Resonite world state.
---

# Resonite Live Send Debug

Use this skill only for real ResoniteLink runs. Prefer local tests first, then switch to this skill when the failure depends on a live Windows session or on the resulting Resonite world state.

Warning: cleanup in this workflow can destroy live world results in the current Resonite session and stop matching live-send CLI processes launched from the same repo. Use it only in an explicitly disposable experiment session, or after the user has clearly approved destroying the current `DatasetRoot` and related results.

Do not use this skill for code-only review or static log reading. Use it only when the user wants a real Windows send, a real Resonite world inspection, or a comparison that is invalid without machine-level execution.

## Dataset Defaults

Unless the user asks for a different target, use the Matsumoto dataset `plateau-20202-matsumoto-shi-2020` with adjacent detailed-building meshes `54372778` and `54372788` for document-backed reproductions and comparisons. Those two meshes are the default fixtures because this repo already contains successful live-send evidence for `54372778`, and the current workspace dataset sample includes detailed-building source files for both meshes.

When the task specifically needs `frn` or city-furniture content, use Yokohama mesh `53391530` instead. This repo already contains successful Yokohama live-send logs for that mesh, and the current workspace dataset sample includes the needed `frn` source there, while the Matsumoto default pair above is for building-focused checks.

Do not assume a fixed on-disk cache layout for these fixtures. Resolve the actual local source path from the current dataset resolver behavior, and use the matching `--dry-run` import or live-send logs to confirm that the requested dataset and mesh resolve before running destructive steps.

## Quick Start

Before any destructive live run, clear the local gate first:

1. Run the repo CI-equivalent verification.
2. Run one matching `--dry-run` import for the dataset and mesh code you intend to send.
3. Then continue with the live workflow below.

```powershell
cmd.exe /c "cd /d C:\path\to\repo && bash scripts/verify-ci.sh"
dotnet run --project C:\path\to\repo\src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj -- build --dataset <dataset> --mesh-code <mesh-code> --source local --local-source-path <dataset-root> --dry-run
powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\discover-session.ps1 -TimeoutSeconds 20 -MaxAnnouncements 5
powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset>
powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath <dataset-root> -Dataset <dataset> -MeshCode <mesh-code> -DemTerrainMode heightmap -Connections 8 -LogPrefix send.<mesh-code>.heightmap.1 -NoWait
```

Record the returned `ProcessId`, `StdoutLog`, and `StderrLog` values immediately, then tail those exact files.

## Run Worksheet

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

## Workflow

1. Locate the workspace root.
Look for `scripts/ResoniteAdmin`. Use the bundled skill scripts for execution.

2. Read the skill contract before running.
Read [references/workflow.md](./references/workflow.md). Follow the Windows-process and cleanup rules exactly.

3. Verify the listener and target session.
Resolve the active ResoniteLink listener from UDP `12512` or the in-game session UI. Do not guess the port. Prefer UDP discovery when it yields `sessionID`; otherwise require explicit UI confirmation and record any disagreement as invalid. Match the session by `sessionID` first, then `sessionName`.

4. Invalidate stale test state before every comparison run.
Remove any existing dataset root for the target dataset from the live world, then verify that zero matching roots remain. Poll every 2 seconds for up to 20 seconds before accepting cleanup. Clear local runtime artifacts before the next run. If cleanup is incomplete, treat the run as invalid and redo it.

5. Run the send from Windows, not WSL.
Use the bundled PowerShell scripts under `skills/resonite-live-send-debug/scripts/`. Prefer asynchronous launch with unique log prefixes so the logs can be tailed while the send is still running.

6. Compare like for like.
When isolating mode-sensitive failures, keep dataset, mesh code, connection count, source path, and listener constant. Re-run listener discovery before each comparison run and confirm the same `sessionName` or `sessionID`. Alternate modes such as `heightmap -> cleanup -> mesh -> cleanup -> heightmap` so differences are attributable to the mode, not to accumulated world state.

7. Inspect both logs and world state.
Check `stderr` first, then `stdout`. Distinguish parse or materialization work from live-send stalls. Record the launched PID, whether it is still alive, and its exit code once it exits. Sample logs at least twice with recorded timestamps before calling a hang. When checking world state during a quiet interval, take at least two timestamped snapshots. Do not assume the last visible `Sent city object ...` line is the true stop point.

8. Report facts, not guesses.
State the exact command, listener endpoint, cleanup result, launched PID, exit status, exit code, last timestamped `import` line, last timestamped `live` line, and whether the world contained the expected dataset root, top-level child slots, and suspicious slot component counts. If a run was contaminated by stale state, say so and discard the conclusion.

## Rules

- Do not ask the user to run the live send when you can run it directly.
- Do not compare runs unless world-side cleanup was verified after the previous run.
- Do not treat a redirected stdout gap as a hang until you have ruled out source parsing or other work still advancing in the same process.
- Do not hard-code a ResoniteLink port into source control or into the skill.
- Do not present a conclusion from a `-NoWait` run as final until you have either observed process exit or explicitly labeled the conclusion provisional.
- Do not leave helper processes or dataset roots behind after the experiment ends.
- Do not assume root-only cleanup proves the absence of all orphaned descendants; if orphan contamination is plausible, say so explicitly.
- Do not treat structure-level conclusions after a failed or interrupted run as fully clean; mark them provisional unless you have an orphan audit beyond root removal.

## Bundled Scripts

- `scripts/discover-session.ps1`
Use to capture live ResoniteLink announcements from UDP `12512`.
- `scripts/cleanup-session.ps1`
Use to remove dataset roots from the live world, stop leftover CLI processes, and clear local runtime artifacts.
- `scripts/run-live-send.ps1`
Use to launch one Windows-side live send with explicit logs.
- `scripts/compare-modes.ps1`
Use to run the standard `heightmap -> mesh -> heightmap` comparison with cleanup between runs.

All four paths above are relative to `skills/resonite-live-send-debug/`.

## Outputs

- Keep the per-run stdout and stderr logs under the repo runtime directory with distinct names.
- Summarize each run with:
  - listener endpoint
  - cleanup verification result
  - process status and exit code
  - exact mode and mesh code
  - last timestamped `import` line
  - last timestamped `live` line
  - whether `stderr` was empty
  - world snapshot: dataset root count, top-level child slot names, suspicious slot component counts
  - sampling times for log and world-state observations
  - whether the conclusion is valid or contaminated
