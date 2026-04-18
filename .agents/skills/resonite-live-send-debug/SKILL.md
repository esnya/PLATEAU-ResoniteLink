---
name: resonite-live-send-debug
description: Run and debug PLATEAU-ResoniteLink live-send reproductions against a real ResoniteLink session. Use when the user wants actual machine-level validation instead of simulated tests, including listener discovery, run cleanup, log capture, and inspection of the resulting Resonite world state.
---

# Resonite Live Send Debug

Use this skill only for real ResoniteLink runs. Prefer local tests first, then switch to this skill when the question depends on a live session, a destructive cleanup cycle, or the resulting Resonite world state.

This file is the Coding Agent entrypoint for the live-send workflow in this repository and the authoritative live-send workflow reference for the public helper command surface. Keep detailed operational guidance in [references/workflow.md](./references/workflow.md) and use this file for trigger, guardrail, and output contracts.

## When To Use

- Real live-send reproduction against an actual ResoniteLink listener.
- Validation that requires observing logs, process state, or the resulting live world.
- Session cleanup, root dumps, or headless-session bring-up as part of the verification loop.

## When Not To Use

- Code-only review, static log reading, or documentation work.
- Local/unit/integration tests that already prove the issue without a live session.
- Any task where destructive cleanup of the current dataset root is not acceptable.

## Guardrails

- Treat cleanup as destructive. It can remove live dataset roots, stop matching live-send CLI processes from this repo, and delete local runtime artifacts.
- Do not ask the user to run the live send if you can run it directly.
- Do not compare runs until cleanup has been verified for the relevant dataset root.
- Keep the final successful `DatasetRoot` in place unless cleanup is explicitly requested.
- Treat interrupted or partial runs as provisional unless cleanup and post-run state were both verified.
- When exact runtime behavior, fixtures, environment selection, or reference values matter, use [references/workflow.md](./references/workflow.md) instead of copying assumptions into the run.

## Guide Surface

- Canonical guide: [references/workflow.md](./references/workflow.md)
- Japanese mirror: [references/workflow.ja.md](./references/workflow.ja.md)

Use the guide for:

- recommended datasets and fixture values
- environment-dependent execution choices
- fixed run worksheets and comparison checklists
- component discovery and BoxCollider inspection procedures
- version-scoped readback limits and reference artifacts

## Public Helper Commands

Use only these operator-facing helper scripts directly:

- `scripts/discover-session.ps1`
- `scripts/start-headless-session.ps1`
- `scripts/stop-headless-session.ps1`
- `scripts/cleanup-session.ps1`
- `scripts/dump-root-session.ps1`
- `scripts/run-live-send.ps1`

The shared Windows build resolver remains internal and is not part of the operator-facing command surface.

## Required Outputs

Summarize each live run with:

- listener endpoint
- cleanup verification result
- process status and exit code
- exact mode and mesh code
- last timestamped `import` line
- last timestamped `live` line
- whether `stderr` was empty
- world snapshot summary
- root dump paths
- observation timestamps
- whether the conclusion is valid or contaminated
