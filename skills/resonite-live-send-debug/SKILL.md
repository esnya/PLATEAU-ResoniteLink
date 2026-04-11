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

Do not assume a fixed on-disk cache layout for these fixtures. Resolve the actual local source path from the current dataset resolver behavior, confirm that the dataset root exists on disk, and use prior live-send evidence or repo fixtures to confirm that the requested dataset and mesh resolve before running destructive steps.

## Canonical Procedure

Follow [docs/live-testing.md](../../../docs/live-testing.md) for the repository's live-send procedure. This skill does not redefine cleanup, send, or comparison steps.

Use [references/workflow.md](./references/workflow.md) only for skill-specific defaults, guardrails, and script inventory.

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
