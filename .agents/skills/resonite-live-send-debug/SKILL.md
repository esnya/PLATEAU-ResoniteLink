---
name: resonite-live-send-debug
description: Run and debug PLATEAU-ResoniteLink live-send reproductions against a real ResoniteLink session. Use when the user wants actual machine-level validation instead of simulated tests, including listener discovery, targeted slot removal, log capture, and inspection of the resulting Resonite world state.
---

# Resonite Live Send Debug

Use this skill only for real ResoniteLink runs. Prefer local tests first, then switch to this skill when the question depends on a live session, a destructive cleanup cycle, or the resulting Resonite world state.

This file is the Coding Agent entrypoint for the live-send workflow in this repository and the authoritative live-send workflow reference for the public operator surface. Keep detailed operational guidance in [references/workflow.md](./references/workflow.md) and keep this file focused on trigger, guardrail, and output contracts.

## When To Use

- Real live-send reproduction against an actual ResoniteLink listener.
- Validation that requires observing logs, process state, or the resulting live world.
- Targeted slot removal, slot dumps, or headless-session bring-up as part of the verification loop.

## When Not To Use

- Code-only review, static log reading, or documentation work.
- Local/unit/integration tests that already prove the issue without a live session.
- Any task where destructive cleanup of the current dataset root is not acceptable.

## Guardrails

- Treat slot removal as destructive. It can remove live content from the current world.
- Do not ask the user to run the live send if you can run it directly.
- Do not compare runs until targeted removal has been verified and a post-removal pre-send root dump confirms the base world state.
- Do not run cleanup, root-dump, and live-send steps for the same world in parallel. Removal verification, pre-send dump capture, and each live-send run must stay serialized.
- Do not encode dataset-root, shared-assets, or common-material naming semantics into the tool. Use `dump-slot` and `remove-slot` as thin primitives only.
- Keep the final successful `DatasetRoot` in place unless cleanup is explicitly requested.
- Treat interrupted or partial runs as provisional unless cleanup and post-run state were both verified.
- Use direct `dotnet` commands as the operator surface. Do not recreate thin wrapper scripts or a project-based session tool.
- `start-headless` is part of the direct tool surface. If `--headless-path` is present, resolve the launcher from that path and a few standard nearby candidates only. If `--headless-path` is omitted, try only the standard Windows Steam install root, then let the actual launcher execution decide whether the environment supports it.
- Do not substitute unrelated machine-local copies when the configured headless path is wrong. If the provided path does not contain a launcher, stop and point to the standard installed-path guide in `references/workflow.md`.
- This skill does not bridge environments. If ResoniteLink uses `localhost`, run sender, listener, and headless in the same environment and document that assumption in the run notes.

## Guide Surface

- Canonical guide: [references/workflow.md](./references/workflow.md)
- Japanese mirror: [references/workflow.ja.md](./references/workflow.ja.md)

Use the guide for:

- recommended datasets and fixture values
- fixed run worksheets and comparison checklists
- component discovery and BoxCollider inspection procedures
- version-scoped readback limits and reference artifacts
- direct command examples for the CLI and session tool script

## Operator Surface

Use only these operator-facing direct commands:

- `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- discover-session ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- start-headless ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- stop-headless ...`

In sandboxed environments, these direct commands can still require restore/build escalation. If `dotnet restore`, `dotnet run`, or `dotnet <script>.cs` fails on .NET first-use or permission setup, rerun the same direct command with the required sandbox escalation instead of replacing it with an ad hoc workflow.

## Required Outputs

Summarize each live run with:

- listener endpoint
- slot-removal verification result
- post-removal pre-send dump result
- process status and exit code
- exact mode and mesh code
- last timestamped `import` line
- last timestamped `live` line
- whether `stderr` was empty
- world snapshot summary
- root dump paths
- observation timestamps
- whether the conclusion is valid or contaminated
