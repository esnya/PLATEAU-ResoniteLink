# AGENTS

Read and apply this file when using a coding agent in this repository.

## Scope
This repository builds a .NET 10 CLI-first import pipeline that maps PLATEAU datasets into Resonite-oriented construction data and live ResoniteLink scene updates.

## Working Rules
- Treat English Markdown files as the canonical source. Whenever an English `.md` file changes, update the matching `.ja.md` file in the same change.
- Treat Japanese Markdown files as translation aids only. When an English and Japanese document disagree, follow the English source and then repair the Japanese mirror.
- Keep runtime and SDK assumptions on .NET 10 unless the user explicitly requests an upgrade.
- Keep shared build, package, formatting, lint, and analyzer policy centralized at the repository root. Do not duplicate those settings inside individual project files unless there is a project-specific exception.
- In Codex or similarly sandboxed WSL environments, do not substitute `dotnet format <sln|csproj>` solution/project mode for the repository verification flow. It can fail while opening the MSBuild workspace with `System.Net.Sockets.SocketException (13): Permission denied /tmp/<guid>`. Use the repository verification command sequence from `CONTRIBUTING.md`, including `dotnet format whitespace . --folder --verify-no-changes`.
- Before every push or pull request update, run the verification command sequence documented in `CONTRIBUTING.md`. `CONTRIBUTING.ja.md` is only its translation mirror.
- Run that verification sequence serially. Do not overlap `dotnet build`, `dotnet test`, or similar commands against the same output tree, because concurrent compiler/testhost access can lock `artifacts/build/windows/obj/.../*.dll` and contaminate the result.
- Use `docs/` only for information that code and tests cannot express well, such as requirements, architecture intent, reference notes, and workflow constraints.
- Large improvement plans that need temporary retention must live under `.tmp/plans/` and stay untracked. Do not keep them under `docs/`, do not link or cite them from active documentation as operational truth, and promote only accepted current outcomes into tracked docs, code, or tests.
- Keep PLATEAU terminology and import semantics aligned with `PLATEAU-SDK-for-UNITY` when shaping dataset, tile, and adapter concepts.
- Keep Resonite-specific I/O behind abstractions so CLI orchestration, application logic, and domain models remain testable and host-agnostic.
- Prefer immutable value types for plans, states, snapshots, policies, inputs, outputs, and results.
- Prefer pure transforms for normalization, validation, identity derivation, naming, grouping, ordering, budgeting, and plan construction.
- Avoid ordered lifecycle APIs over shared mutable objects when a value-based execution contract can express the same behavior.
- Keep mutable state localized to narrow boundary adapters for transport, filesystem, network, caching, logging/progress, and cancellation.
- Treat transport and target integration as immutable-by-default: prefer read-once/create-only flows, and isolate unavoidable update operations in dedicated adapter layers.
- Do not introduce new broad behavior-oriented types such as `Builder`, `Manager`, `Coordinator`, `Helper`, or `Util` unless the user explicitly requests that pattern.
- Prefer deterministic outputs, explicit command inputs, and reproducible local/CI behavior.
- Add or update automated tests when behavior changes.
- Do not treat grep-based architecture or naming tests as the canonical boundary guard. Keep naming and ownership rules here, enforce dependency direction with project references, and cover only observable behavior with automated tests.
- Keep dependency injection flowing through the full stack. Core, application, import, bootstrap, target, and transport code must not hide concrete defaults behind `new`, static factories, or fallback self-wiring.
- Keep legacy conversions and static projection helpers in adapter-edge code only. Core concepts and neutral contracts must not depend on `ToLegacy`, `FromLegacy`, or target-specific mapper utilities.
- Keep result models pure. Do not store bootstrap-only, discovery-only, connection-only, or layout-only state on document/read results when a separate context or snapshot can carry it.
- When renaming concepts, update directories, filenames, namespaces, project names, resources, and docs in the same cut. Do not leave compatibility aliases behind.
- Keep namespace declarations aligned to folder ownership boundaries. When changing ownership boundaries, update namespaces and directory layout in one cut so the source path can be read as the ownership signal.
- Do not keep global using directives in the final-state architecture. Declare dependencies in each file so cross-boundary usage stays explicit and mechanically reviewable.
- Keep internal contracts target-neutral. Internal models must not depend on Resonite-specific vector semantics; keep target-specific conversions inside dedicated adapter-edge converters.
- Prefer `ILogger<T>` and framework-integrated observability over custom logging pipelines when evolving diagnostics, and use `System.Diagnostics.Metrics` for first-class metrics instrumentation.

- Keep auxiliary git worktrees under `<repo>/.worktree/`, and avoid sibling directories or `/tmp` worktrees; this keeps ephemeral worktrees consistently ignored and separated from the main checkout.

## Live Send Workflow
- For Coding Agent live tests, follow [.agents/skills/resonite-live-send-debug/SKILL.md](.agents/skills/resonite-live-send-debug/SKILL.md).
