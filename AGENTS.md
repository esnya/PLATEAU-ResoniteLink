# AGENTS

Read and apply this file when using a coding agent in this repository.

## Scope
This repository builds a .NET 10 import pipeline that maps PLATEAU datasets into Resonite-oriented construction data and, later, live Resonite adapters.

## Working Rules
- Treat English Markdown files as the canonical source. Whenever an English `.md` file changes, update the matching `.ja.md` file in the same change.
- Keep runtime and SDK assumptions on .NET 10 unless the user explicitly requests an upgrade.
- Keep shared build, package, formatting, lint, and analyzer policy centralized at the repository root. Do not duplicate those settings inside individual project files unless there is a project-specific exception.
- In Codex or similarly sandboxed WSL environments, do not rely on `dotnet format <sln|csproj>` solution/project mode for verification. It can fail while opening the MSBuild workspace with `System.Net.Sockets.SocketException (13): Permission denied /tmp/<guid>`. Use `dotnet format whitespace . --folder --verify-no-changes` for whitespace verification, and use `dotnet test Plateau.ResoniteLink.sln --configuration Release -m:1 -p:UseSharedCompilation=false` to enforce analyzer and code-style rules through build.
- Before every push or pull request update, run `bash scripts/verify-ci.sh`. For agents, keep the sequence explicit and in order: `dotnet restore Plateau.ResoniteLink.sln --locked-mode`, `dotnet format whitespace . --folder --verify-no-changes`, `dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false`, then `dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 -p:UseSharedCompilation=false`.
- Use `docs/` only for information that code and tests cannot express well, such as requirements, architecture intent, reference notes, and workflow constraints.
- Keep PLATEAU terminology and import semantics aligned with `PLATEAU-SDK-for-UNITY` when shaping dataset, tile, and adapter concepts.
- Keep Resonite-specific I/O behind abstractions so CLI orchestration, application logic, and domain models remain testable and host-agnostic.
- Prefer deterministic outputs, explicit command inputs, and reproducible local/CI behavior.
- Add or update automated tests when behavior changes.

## Live Send Workflow
- Follow the concrete live-send and Resonite UnitySDK `AutoDiscovery` workflow in [docs/live-testing.md](docs/live-testing.md).
