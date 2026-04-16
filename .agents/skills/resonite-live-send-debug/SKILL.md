---
name: resonite-live-send-debug
description: Run and debug PLATEAU-ResoniteLink live-send reproductions against a real ResoniteLink session. Use when the user wants actual machine-level validation instead of simulated tests, including listener discovery, DatasetRoot cleanup between runs, alternating `heightmap` and `mesh` comparisons, log capture, and inspection of the generated Resonite world state.
---

# Resonite Live Send Debug

Use this skill only for real ResoniteLink runs. Prefer local tests first, then switch to this skill when the failure depends on a live session or on the resulting Resonite world state.

Warning: cleanup in this workflow can destroy live world results in the current Resonite session and stop matching live-send CLI processes launched from the same repo. Use it only in an explicitly disposable experiment session, or after the user has clearly approved destroying the current `DatasetRoot` and related results.

Do not use this skill for code-only review or static log reading. Use it only when the user wants a real live send, a real Resonite world inspection, or a comparison that is invalid without machine-level execution.

Use this file as the Coding Agent execution playbook for live-send runs. Keep the live-send source of truth inside this skill package: use this file for execution heuristics and [references/workflow.md](./references/workflow.md) for the canonical operator-facing procedure.

## Dataset Defaults

Treat the dataset and mesh choices in this section as recommended starting-point parameters, not as procedural requirements.

Unless the user asks for a different target, use the Matsumoto dataset `plateau-20202-matsumoto-shi-2020` with adjacent detailed-building meshes `54372778` and `54372788` for document-backed reproductions and comparisons. Those two meshes are the default fixtures because this repo already contains successful live-send evidence for `54372778`, and the current workspace dataset sample includes detailed-building source files for both meshes.

When the task specifically needs `frn` or city-furniture content, use Yokohama mesh `53391530` instead. This repo already contains successful Yokohama live-send logs for that mesh, and the current workspace dataset sample includes the needed `frn` source there, while the Matsumoto default pair above is for building-focused checks.

Do not assume a fixed on-disk cache layout for these fixtures. Resolve the actual local source path from the current dataset resolver behavior, confirm that the dataset root exists on disk, and use prior live-send evidence or repo fixtures to confirm that the requested dataset and mesh resolve before running destructive steps.

## Canonical Procedure

Follow [references/workflow.md](./references/workflow.md) for the canonical live-send procedure. This file keeps the Coding Agent guardrails, defaults, and run worksheet, while the reference file carries the procedural steps and command surface.

Execution heuristics for sender placement:

- When the target listener is not reachable from WSL via `localhost`, run wrapped helpers from Windows.
- If sender and listener are same-host and reachable from WSL via `localhost`, a WSL-driven run is valid.
- If a reverse proxy or bridge rewrites to an acceptable host for the listener, an IP route can be used when observed reachability and session identity are valid.
- Decide by actual reachability and observed listener behavior; avoid hardcoding Windows-only or WSL-only assumptions.

For disposable headless validation, prefer this operator sequence:

1. `start-headless-session.ps1`
2. `dump-root-session.ps1 -Label baseline`
3. `cleanup-session.ps1`
4. `run-live-send.ps1`
5. `dump-root-session.ps1 -Label after-send`
6. `stop-headless-session.ps1`

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

## Rules

- Do not ask the user to run the live send when you can run it directly.
- Do not compare runs unless world-side cleanup was verified after the previous run.
- Do not treat a redirected stdout gap as a hang until you have ruled out source parsing or other work still advancing in the same process.
- Do not hard-code a ResoniteLink port into source control or into the skill.
- Do not present a conclusion from a `-NoWait` run as final until you have either observed process exit or explicitly labeled the conclusion provisional.
- Keep successful final `DatasetRoot` artifacts in place by default for visual inspection unless cleanup is explicitly requested.
- Do not assume root-only cleanup proves the absence of all orphaned descendants; if orphan contamination is plausible, say so explicitly.
- Do not treat structure-level conclusions after a failed or interrupted run as fully clean; mark them provisional unless you have an orphan audit beyond root removal.

## Bundled Scripts

- `scripts/discover-session.ps1`
Use to capture live ResoniteLink announcements from UDP `12512`.
- `scripts/start-headless-session.ps1`
Use to launch a disposable Windows headless session directly and verify the announced ResoniteLink port.
- `scripts/stop-headless-session.ps1`
Use to stop the tracked headless PID launched for the experiment, or an explicit PID.
- `scripts/dump-root-session.ps1`
Use to capture a recursive Root snapshot from the tracked or explicitly addressed session.
- `scripts/cleanup-session.ps1`
Use to remove dataset roots from the live world, stop leftover CLI processes, and clear local runtime artifacts.
- `scripts/run-live-send.ps1`
Use to launch one live send with explicit logs.
- `scripts/compare-modes.ps1`
Use to run the standard `heightmap -> mesh -> heightmap` comparison with cleanup between runs.

All seven paths above are relative to `.agents/skills/resonite-live-send-debug/`.

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
  - root dump paths for any baseline or post-send snapshots
  - sampling times for log and world-state observations
  - whether the conclusion is valid or contaminated
