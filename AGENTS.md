# AGENTS

Read and apply this file when using a coding agent in this repository.

## Scope
This repository builds a .NET 10 CLI-first import pipeline that maps PLATEAU datasets into Resonite-oriented construction data and live ResoniteLink scene updates.

## Working Rules
- Treat English Markdown files as the canonical source. Whenever an English `.md` file changes, update the matching `.ja.md` file in the same change.
- Treat Japanese Markdown files as translation aids only. When an English and Japanese document disagree, follow the English source and then repair the Japanese mirror.
- Keep runtime and SDK assumptions on .NET 10 unless the user explicitly requests an upgrade.
- Keep shared build, package, formatting, lint, and analyzer policy centralized at the repository root. Do not duplicate those settings inside individual project files unless there is a project-specific exception.
- In Codex or similarly sandboxed WSL environments, do not substitute `dotnet format <sln|csproj>` solution/project mode for the repository verification flow. It can fail while opening the MSBuild workspace with `System.Net.Sockets.SocketException (13): Permission denied /tmp/<guid>`. Keep the repository-standard verification entrypoint as `bash scripts/verify-ci.sh`; the whitespace check behind that flow is `dotnet format whitespace . --folder --verify-no-changes`.
- Before every push or pull request update, run `bash scripts/verify-ci.sh`. `CONTRIBUTING.md` is the contributor-facing source of truth for that workflow; `CONTRIBUTING.ja.md` is only its translation mirror. Do not re-document or reorder the script's internal command sequence elsewhere.
- Use `docs/` only for information that code and tests cannot express well, such as requirements, architecture intent, reference notes, and workflow constraints.
- Large improvement plans that need temporary retention must live under `.tmp/plans/` and stay untracked. Do not keep them under `docs/`, do not link or cite them from active documentation as operational truth, and promote only accepted current outcomes into tracked docs, code, or tests.
- Keep PLATEAU terminology and import semantics aligned with `PLATEAU-SDK-for-UNITY` when shaping dataset, tile, and adapter concepts.
- Keep Resonite-specific I/O behind abstractions so CLI orchestration, application logic, and domain models remain testable and host-agnostic.
- Prefer deterministic outputs, explicit command inputs, and reproducible local/CI behavior.
- Add or update automated tests when behavior changes.

- Keep auxiliary git worktrees under `<repo>/.worktree/`, and avoid sibling directories or `/tmp` worktrees; this keeps ephemeral worktrees consistently ignored and separated from the main checkout.

## Live Send Workflow
- Follow the concrete live-send and Resonite UnitySDK `AutoDiscovery` workflow in [docs/live-testing.md](docs/live-testing.md). Treat that English file as the source of truth for live-send procedures, and [docs/live-testing.ja.md](docs/live-testing.ja.md) as a translation mirror only.
