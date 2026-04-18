---
name: resonite-live-send-debug
description: Run and debug PLATEAU-ResoniteLink live-send reproductions against a real ResoniteLink session. Use when the user wants actual machine-level validation instead of simulated tests, including listener discovery, run cleanup, log capture, and inspection of the resulting Resonite world state.
---

# Resonite Live Send Debug

Use this skill only for real ResoniteLink runs. Prefer local tests first, then switch to this skill when the question depends on a live session, a destructive cleanup cycle, or the resulting Resonite world state.

This file is the Coding Agent entrypoint for the live-send workflow in this repository and the authoritative live-send workflow reference for the public operator surface. Keep detailed operational guidance in [references/workflow.md](./references/workflow.md) and keep this file focused on trigger, guardrail, and output contracts.

## When To Use

- Real live-send reproduction against an actual ResoniteLink listener.
- Validation that requires observing logs, process state, or the resulting live world.
- Session cleanup, root dumps, or headless-session bring-up as part of the verification loop.

## When Not To Use

- Code-only review, static log reading, or documentation work.
- Local/unit/integration tests that already prove the issue without a live session.
- Any task where destructive cleanup of the current dataset root is not acceptable.

## Guardrails

- Treat cleanup as destructive. It can remove live dataset roots and delete local runtime artifacts under `runtime/windows/resonite`.
- Do not ask the user to run the live send if you can run it directly.
- Do not compare runs until cleanup has been verified for the relevant dataset root.
- Keep the final successful `DatasetRoot` in place unless cleanup is explicitly requested.
- Treat interrupted or partial runs as provisional unless cleanup and post-run state were both verified.
- Use direct `dotnet run --project ...` commands as the operator surface. Do not recreate thin wrapper scripts.
- `--start-headless` is part of the direct tool surface, but the actual headless launcher path can still be Windows-only. Let the tool reject unsupported environments explicitly instead of routing through WSL-to-Windows helpers.

## Guide Surface

- Canonical guide: [references/workflow.md](./references/workflow.md)
- Japanese mirror: [references/workflow.ja.md](./references/workflow.ja.md)

Use the guide for:

- recommended datasets and fixture values
- fixed run worksheets and comparison checklists
- component discovery and BoxCollider inspection procedures
- version-scoped readback limits and reference artifacts
- direct command examples for the CLI and session tool

## Operator Surface

Use only these operator-facing direct commands:

- `dotnet run --project src/Plateau.ResoniteLink.Cli/Plateau.ResoniteLink.Cli.csproj -- build ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --discover-session ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --dump-root ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --remove-slot ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --cleanup-dataset-root ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --start-headless ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --stop-headless ...`

In sandboxed environments, these direct commands can still require restore/build escalation. If `dotnet restore` or `dotnet run` fails on .NET first-use or permission setup, rerun the same direct command with the required sandbox escalation instead of replacing it with an ad hoc workflow.

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
